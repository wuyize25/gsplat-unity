// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public interface IGsplat
    {
        public Transform transform { get; }
        public uint SplatCount { get; }
        public ISorterResource SorterResource { get; }
        public bool isActiveAndEnabled { get; }
        public bool Valid { get; }
    }

    public interface ISorterResource
    {
        public GraphicsBuffer PositionBuffer { get; }
        public GraphicsBuffer OrderBuffer { get; }
        public void Dispose();
    }
    
    // some codes of this class originated from the GaussianSplatRenderSystem in aras-p/UnityGaussianSplatting by Aras Pranckevičius
    // https://github.com/aras-p/UnityGaussianSplatting/blob/main/package/Runtime/GaussianSplatRenderer.cs
    public class GsplatSorter
    {
        class Resource : ISorterResource
        {
            public GraphicsBuffer PositionBuffer { get; }
            public GraphicsBuffer OrderBuffer { get; }

            public GraphicsBuffer InputKeys { get; private set; }
            public GsplatSortPass.SupportResources Resources { get; }
            public bool Initialized;

            public Resource(uint count, GraphicsBuffer positionBuffer, GraphicsBuffer orderBuffer)
            {
                PositionBuffer = positionBuffer;
                OrderBuffer = orderBuffer;

                InputKeys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)count, sizeof(uint));
                Resources = GsplatSortPass.SupportResources.Load(count);
            }

            public void Dispose()
            {
                InputKeys?.Dispose();
                Resources.Dispose();

                InputKeys = null;
            }
        }
        
        public static GsplatSorter Instance => s_instance ??= new GsplatSorter();
        static GsplatSorter s_instance;
        CommandBuffer m_commandBuffer;
        readonly HashSet<IGsplat> m_gsplats = new();
        readonly HashSet<Camera> m_camerasInjected = new();
        readonly List<IGsplat> m_activeGsplats = new();
        GsplatSortPass m_sortPass;
        public const string k_PassName = "SortGsplats";

        public bool Valid => m_sortPass is { Valid: true };

        public void InitSorter(ComputeShader computeShader)
        {
            m_sortPass = computeShader ? new GsplatSortPass(computeShader) : null;
        }

        public void RegisterGsplat(IGsplat gsplat)
        {
            if (m_gsplats.Count == 0)
            {
                if (!GraphicsSettings.currentRenderPipeline)
                    Camera.onPreCull += OnPreCullCamera;
            }

            m_gsplats.Add(gsplat);
        }

        public void UnregisterGsplat(IGsplat gsplat)
        {
            if (!m_gsplats.Remove(gsplat))
                return;
            if (m_gsplats.Count != 0) return;

            if (m_camerasInjected != null)
            {
                if (m_commandBuffer != null)
                    foreach (var cam in m_camerasInjected.Where(cam => cam))
                        cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, m_commandBuffer);
                m_camerasInjected.Clear();
            }

            m_activeGsplats.Clear();
            m_commandBuffer?.Dispose();
            m_commandBuffer = null;
            Camera.onPreCull -= OnPreCullCamera;
        }

        public bool GatherGsplatsForCamera(Camera cam)
        {
            if (cam.cameraType == CameraType.Preview)
                return false;

            m_activeGsplats.Clear();
            foreach (var gs in m_gsplats.Where(gs => gs is { isActiveAndEnabled: true, Valid: true }))
                m_activeGsplats.Add(gs);
            return m_activeGsplats.Count != 0;
        }

        void InitialClearCmdBuffer(Camera cam)
        {
            m_commandBuffer ??= new CommandBuffer { name = k_PassName };
            if (!GraphicsSettings.currentRenderPipeline && cam &&
                !m_camerasInjected.Contains(cam))
            {
                cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, m_commandBuffer);
                m_camerasInjected.Add(cam);
            }

            m_commandBuffer.Clear();
        }

        void OnPreCullCamera(Camera camera)
        {
            if (!Valid || !GsplatSettings.Instance.Valid || !GatherGsplatsForCamera(camera))
                return;

            InitialClearCmdBuffer(camera);
            DispatchSort(m_commandBuffer, camera);
        }

        public void DispatchSort(CommandBuffer cmd, Camera camera)
        {
            foreach (var gs in m_activeGsplats)
            {
                var res = (Resource)gs.SorterResource;
                if (!res.Initialized)
                {
                    m_sortPass.InitPayload(cmd, res.OrderBuffer, gs.SplatCount);
                    res.Initialized = true;
                }

                var sorterArgs = new GsplatSortPass.Args
                {
                    Count = gs.SplatCount,
                    MatrixMv = camera.worldToCameraMatrix * gs.transform.localToWorldMatrix,
                    PositionBuffer = res.PositionBuffer,
                    InputKeys = res.InputKeys,
                    InputValues = res.OrderBuffer,
                    Resources = res.Resources
                };
                m_sortPass.Dispatch(cmd, sorterArgs);
            }
        }

        public ISorterResource CreateSorterResource(uint count, GraphicsBuffer positionBuffer,
            GraphicsBuffer orderBuffer)
        {
            return new Resource(count, positionBuffer, orderBuffer);
        }
    }
}