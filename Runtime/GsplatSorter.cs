// Copyright (c) 2025 Yize Wu
// Copyright (c) 2026 Keir Rice
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
        public uint RemainingCount { get; }
        public ISorterResource SorterResource { get; }
        public bool isActiveAndEnabled { get; }
        public bool Valid { get; }
        public bool ComputeSortRequired { get; }
        public void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv);

        // Used by GsplatSorter to populate the global packed buffer.
        public GsplatResource GsplatResource { get; }
        public uint SplatCount { get; }
        public byte SHBands { get; }
    }

    public interface ISorterResource
    {
        public GraphicsBuffer OrderBuffer { get; }
        public GraphicsBuffer InputKeys { get; }
        public bool Initialized { get; set; }
        public void Dispose();
    }

    // some codes of this class originated from the GaussianSplatRenderSystem in aras-p/UnityGaussianSplatting by Aras Pranckevičius
    // https://github.com/aras-p/UnityGaussianSplatting/blob/main/package/Runtime/GaussianSplatRenderer.cs
    public class GsplatSorter
    {
        class Resource : ISorterResource
        {
            public GraphicsBuffer OrderBuffer { get; }

            public GraphicsBuffer InputKeys { get; private set; }
            public GsplatSortPass.SupportResources Resources { get; }
            public bool Initialized { get; set; }

            public Resource(uint count, GraphicsBuffer orderBuffer)
            {
                OrderBuffer = orderBuffer;
                InputKeys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)count, sizeof(uint));
                Resources = GsplatSortPass.SupportResources.Load(count);
                Initialized = false;
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
        readonly HashSet<int> m_warnedUncompressed = new();
        GsplatSortPass m_sortPass;
        public const string k_PassName = "SortGsplats";

        readonly GsplatGlobalRenderer m_globalRenderer = new();

        /// <summary>True when 2+ active renderers are being globally merged this frame.</summary>
        public bool GlobalRenderEnabled { get; private set; }

        static readonly ProfilingSampler k_samplerDepth = new("Gsplat.DepthPerRenderer");
        static readonly ProfilingSampler k_samplerSort = new("Gsplat.SortPerRenderer");

        public bool Valid => m_sortPass is { Valid: true };

        public void InitSorter(ComputeShader computeShader)
        {
            m_sortPass = computeShader ? new GsplatSortPass(computeShader) : null;
        }

        public void InitGlobal(GsplatGlobalMaterial globalMaterial)
        {
            m_globalRenderer.InitGlobal(globalMaterial);
        }

        public void RegisterGsplat(IGsplat gsplat)
        {
            if (m_gsplats.Count == 0)
            {
                if (!GraphicsSettings.currentRenderPipeline)
                    Camera.onPreCull += OnPreCullCamera;
            }

            m_gsplats.Add(gsplat);
            m_globalRenderer.MarkGlobalBuffersDirty();
        }

        public void UnregisterGsplat(IGsplat gsplat)
        {
            if (!m_gsplats.Remove(gsplat))
                return;

            if (gsplat is UnityEngine.Object obj)
                m_warnedUncompressed.Remove(obj.GetInstanceID());

            m_globalRenderer.MarkGlobalBuffersDirty();

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
            m_globalRenderer.DisposeGlobalBuffers();
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

        // Decides scene-wide whether the global merge can run this frame. 
        bool CanRenderGlobally()
        {
            // renderer_id is packed into 8 bits ([31:24]); max 255 renderers.
            if (m_activeGsplats.Count > 255)
            {
                Debug.LogError(
                    "[GsplatSorter] Global merge supports at most 255 renderers. Falling back to per-renderer rendering.");
                return false;
            }

            // Global merge requires every active renderer to use SPARK compression.
            foreach (var gs in m_activeGsplats)
            {
                if (gs.GsplatResource is GsplatResourceSpark) continue;
                var obj = gs as UnityEngine.Object;
                var id = obj ? obj.GetInstanceID() : 0;
                if (m_warnedUncompressed.Add(id))
                    Debug.LogWarning(
                        $"[GsplatSorter] '{obj?.name}' uses an uncompressed asset; global sort requires every active renderer to use SPARK compression. Disabling global sort for this scene — all renderers fall back to per-renderer rendering.");
                return false;
            }

            return true;
        }

        public void MarkGlobalBuffersDirty() => m_globalRenderer.MarkGlobalBuffersDirty();

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
            // --- Per-renderer depth computation ---
            cmd.BeginSample(k_samplerDepth.name);
            foreach (var gs in m_activeGsplats)
            {
                if (gs.RemainingCount <= 0) continue;
                gs.ComputeDepth(cmd, camera.worldToCameraMatrix * gs.transform.localToWorldMatrix);
            }

            cmd.EndSample(k_samplerDepth.name);

            // --- Per-renderer radix sort ---
            cmd.BeginSample(k_samplerSort.name);
            foreach (var gs in m_activeGsplats)
            {
                if (gs.SorterResource is not Resource res) continue;
                if (!gs.ComputeSortRequired || gs.RemainingCount <= 0)
                    continue;

                if (!res.Initialized)
                {
                    m_sortPass.InitPayload(cmd, res.OrderBuffer, (uint)res.OrderBuffer.count);
                    res.Initialized = true;
                }

                m_sortPass.Dispatch(cmd, new GsplatSortPass.Args
                {
                    Count = gs.RemainingCount,
                    MatrixMv = camera.worldToCameraMatrix * gs.transform.localToWorldMatrix,
                    InputKeys = res.InputKeys,
                    InputValues = res.OrderBuffer,
                    Resources = res.Resources
                });
            }

            cmd.EndSample(k_samplerSort.name);

            // --- Global K-way merge ---
            if (GlobalRenderEnabled)
                m_globalRenderer.DispatchMerge(cmd, m_activeGsplats);
        }

        // Called by GsplatPlayerLoopHook once per frame, before Unity's PostLateUpdate phase
        public void Update()
        {
            GlobalRenderEnabled = m_globalRenderer.Valid && GsplatSettings.Instance.EnableGlobalSort &&
                                  m_activeGsplats.Count >= 2;
            if (!GlobalRenderEnabled) return;
            m_activeGsplats.Clear();
            foreach (var gs in m_gsplats.Where(gs => gs is { isActiveAndEnabled: true, Valid: true }))
                m_activeGsplats.Add(gs);
            GlobalRenderEnabled = GlobalRenderEnabled && CanRenderGlobally();
            if (!GlobalRenderEnabled) return;
            m_globalRenderer.Update(m_activeGsplats);
        }

        public ISorterResource CreateSorterResource(uint count, GraphicsBuffer orderBuffer)
        {
            return new Resource(count, orderBuffer);
        }
    }
}