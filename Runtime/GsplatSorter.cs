using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public interface IGsplat
    {
        public Transform transform { get; }
        public uint SplatCount { get; }
        public SorterResource SorterResource { get; }
        public bool isActiveAndEnabled { get; }
        public bool Valid { get; }
    }

    public class SorterResource
    {
        public GraphicsBuffer PositionBuffer;
        public GraphicsBuffer OrderBuffer;

        internal GraphicsBuffer InputKeys;
        internal GsplatSortPass.SupportResources Resources;

        public SorterResource(uint count, GraphicsBuffer positionBuffer, GraphicsBuffer orderBuffer)
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

            PositionBuffer = null;
            OrderBuffer = null;
            InputKeys = null;
        }
    }

    public class GsplatSorter
    {
        public static GsplatSorter Instance => s_instance ??= new GsplatSorter();
        static GsplatSorter s_instance;
        CommandBuffer m_commandBuffer;
        readonly HashSet<IGsplat> m_gsplats = new();
        readonly HashSet<Camera> m_camerasInjected = new();
        readonly List<IGsplat> m_activeGsplats = new();
        GsplatSortPass m_sortPass;

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
            // TODO: InitPayload here
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

        bool GatherSplatsForCamera(Camera cam)
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
            m_commandBuffer ??= new CommandBuffer { name = "SortGsplats" };
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
            if (!Valid || !GsplatSettings.Instance.Valid || !GatherSplatsForCamera(camera))
                return;

            InitialClearCmdBuffer(camera);

            foreach (var gs in m_activeGsplats)
            {
                var sorterArgs = new GsplatSortPass.Args
                {
                    Count = gs.SplatCount,
                    MatrixMv = camera.worldToCameraMatrix * gs.transform.localToWorldMatrix,
                    PositionBuffer = gs.SorterResource.PositionBuffer,
                    InputKeys = gs.SorterResource.InputKeys,
                    InputValues = gs.SorterResource.OrderBuffer,
                    Resources = gs.SorterResource.Resources
                };
                m_sortPass.Dispatch(m_commandBuffer, sorterArgs);
            }
        }
    }
}