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
        public GraphicsBuffer PackedSplatsBuffer { get; }
        public GraphicsBuffer SH1Buffer { get; }
        public GraphicsBuffer SH2Buffer { get; }
        public GraphicsBuffer SH3Buffer { get; }
        public GraphicsBuffer SH4Buffer { get; }
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

        // -----------------------------------------------------------------------
        // Singleton
        // -----------------------------------------------------------------------
        public static GsplatSorter Instance => s_instance ??= new GsplatSorter();
        static GsplatSorter s_instance;

        CommandBuffer m_commandBuffer;
        readonly HashSet<IGsplat> m_gsplats = new();
        readonly HashSet<Camera> m_camerasInjected = new();
        readonly List<IGsplat> m_activeGsplats = new();
        readonly HashSet<int> m_warnedUncompressed = new();
        GsplatSortPass m_sortPass;
        public const string k_PassName = "SortGsplats";

        // -----------------------------------------------------------------------
        // Global merge state
        // -----------------------------------------------------------------------
        GsplatGlobalMaterial m_globalMaterial;

        int m_kernelPackOrders        = -1;
        int m_kernelMergeTwo          = -1;
        int m_kernelMergeTwoWithDepth = -1;
        int m_kernelCopyUint4         = -1;
        int m_kernelCopyUint2         = -1;

        // CPU-side per-renderer metadata (rebuilt when renderers change).
        uint[] m_rendererOffsets;   // splat start index in global buffers, per active renderer
        uint   m_totalSplatCount;
        uint   m_totalRemainingCount; // sum of RemainingCounts; valid entries in GlobalOrderBuffer after merge
        byte   m_globalSHBands;
        bool   m_globalBuffersDirty = true;

        // Global packed data buffers (sub-ranges owned by each renderer).
        GraphicsBuffer m_globalPackedBuffer;
        GraphicsBuffer m_globalSH1Buffer;
        GraphicsBuffer m_globalSH2Buffer;
        GraphicsBuffer m_globalSH3Buffer;
        GraphicsBuffer m_globalSH4Buffer;
        GraphicsBuffer m_globalOrderBuffer;
        GraphicsBuffer m_rendererOffsetsBuffer;
        GraphicsBuffer m_rendererTransformsBuffer;

        // Scratch buffers for merge passes.
        // m_packScratch*  : all renderers' orders+depths packed and concatenated.
        // m_mergeScratch* : ping-pong pair for cascaded intermediate merges.
        GraphicsBuffer m_packScratchOrders;
        GraphicsBuffer m_packScratchDepths;
        GraphicsBuffer m_mergeScratchOrders;
        GraphicsBuffer m_mergeScratchDepths;
        uint           m_scratchCapacity;
        Matrix4x4[]    m_rendererTransformsCache;
        MaterialPropertyBlock m_globalPropertyBlock;

        /// <summary>True when 2+ active renderers are being globally merged this frame.</summary>
        public bool GlobalRenderEnabled { get; private set; }

        // -----------------------------------------------------------------------
        // Profiling samplers
        // -----------------------------------------------------------------------
        static readonly ProfilingSampler k_samplerDepth = new("Gsplat.DepthPerRenderer");
        static readonly ProfilingSampler k_samplerSort  = new("Gsplat.SortPerRenderer");
        static readonly ProfilingSampler k_samplerMerge = new("Gsplat.MergeKWay");
        static readonly ProfilingSampler k_samplerDraw  = new("Gsplat.DrawAll");

        // -----------------------------------------------------------------------
        // Shader property IDs — merge / pack
        // -----------------------------------------------------------------------
        static readonly int k_rawOrders      = Shader.PropertyToID("_RawOrders");
        static readonly int k_rawDepths      = Shader.PropertyToID("_RawDepths");
        static readonly int k_packedOrders   = Shader.PropertyToID("_PackedOrders");
        static readonly int k_packedDepths   = Shader.PropertyToID("_PackedDepths");
        static readonly int k_rendererIdx    = Shader.PropertyToID("_RendererIdx");
        static readonly int k_srcCount       = Shader.PropertyToID("_SrcCount");
        static readonly int k_dstOffset      = Shader.PropertyToID("_DstOffset");

        static readonly int k_depthsA        = Shader.PropertyToID("_DepthsA");
        static readonly int k_ordersA        = Shader.PropertyToID("_OrdersA");
        static readonly int k_depthsB        = Shader.PropertyToID("_DepthsB");
        static readonly int k_ordersB        = Shader.PropertyToID("_OrdersB");
        static readonly int k_offsetA        = Shader.PropertyToID("_OffsetA");
        static readonly int k_offsetB        = Shader.PropertyToID("_OffsetB");
        static readonly int k_countA         = Shader.PropertyToID("_CountA");
        static readonly int k_countB         = Shader.PropertyToID("_CountB");
        static readonly int k_outputOffset   = Shader.PropertyToID("_OutputOffset");
        static readonly int k_outputOrders   = Shader.PropertyToID("_OutputOrders");
        static readonly int k_outputDepths   = Shader.PropertyToID("_OutputDepths");

        // Copy kernel IDs
        static readonly int k_srcUint4         = Shader.PropertyToID("_SrcUint4");
        static readonly int k_dstUint4         = Shader.PropertyToID("_DstUint4");
        static readonly int k_srcUint2         = Shader.PropertyToID("_SrcUint2");
        static readonly int k_dstUint2         = Shader.PropertyToID("_DstUint2");
        static readonly int k_srcElementCount  = Shader.PropertyToID("_SrcElementCount");
        static readonly int k_dstElementOffset = Shader.PropertyToID("_DstElementOffset");

        // Global draw material properties
        static readonly int k_globalOrderBuffer      = Shader.PropertyToID("_GlobalOrderBuffer");
        static readonly int k_globalPackedBuffer     = Shader.PropertyToID("_GlobalPackedBuffer");
        static readonly int k_globalSH1Buffer        = Shader.PropertyToID("_GlobalSH1Buffer");
        static readonly int k_globalSH2Buffer        = Shader.PropertyToID("_GlobalSH2Buffer");
        static readonly int k_globalSH3Buffer        = Shader.PropertyToID("_GlobalSH3Buffer");
        static readonly int k_globalSH4Buffer        = Shader.PropertyToID("_GlobalSH4Buffer");
        static readonly int k_shDegree               = Shader.PropertyToID("_SHDegree");
        static readonly int k_rendererOffsetsProp    = Shader.PropertyToID("_RendererOffsets");
        static readonly int k_rendererTransformsProp = Shader.PropertyToID("_RendererTransforms");
        static readonly int k_totalSplatCount        = Shader.PropertyToID("_TotalSplatCount");
        static readonly int k_splatInstanceSize      = Shader.PropertyToID("_SplatInstanceSize");
        static readonly int k_brightness             = Shader.PropertyToID("_Brightness");
        static readonly int k_scaleFactor            = Shader.PropertyToID("_ScaleFactor");
        static readonly int k_gammaToLinear          = Shader.PropertyToID("_GammaToLinear");

        // -----------------------------------------------------------------------
        // Validity
        // -----------------------------------------------------------------------
        public bool Valid => m_sortPass is { Valid: true };

        bool GlobalValid => m_globalMaterial
                           && m_globalMaterial.MergeShader && m_globalMaterial.CopyBufferShader
                           && m_globalMaterial.DefaultMaterial
                           && m_kernelPackOrders >= 0
                           && m_kernelMergeTwo >= 0
                           && m_kernelMergeTwoWithDepth >= 0
                           && m_kernelCopyUint4 >= 0
                           && m_kernelCopyUint2 >= 0;

        // -----------------------------------------------------------------------
        // Init
        // -----------------------------------------------------------------------
        public void InitSorter(ComputeShader computeShader)
        {
            m_sortPass = computeShader ? new GsplatSortPass(computeShader) : null;
        }

        public void InitGlobal(GsplatGlobalMaterial globalMaterial)
        {
            m_globalMaterial     = globalMaterial;
            m_kernelPackOrders   = -1;
            m_kernelMergeTwo     = -1;
            m_kernelMergeTwoWithDepth = -1;
            m_kernelCopyUint4    = -1;
            m_kernelCopyUint2    = -1;

            var mergeShader = globalMaterial ? globalMaterial.MergeShader : null;
            var copyShader  = globalMaterial ? globalMaterial.CopyBufferShader : null;

            if (mergeShader)
            {
                if (!mergeShader.HasKernel("PackRendererOrders"))
                    Debug.LogError($"[GsplatSorter] MergeShader '{mergeShader.name}' is missing kernel 'PackRendererOrders' — assign GsplatMergeOrderBuffers.compute.");
                else
                {
                    m_kernelPackOrders        = mergeShader.FindKernel("PackRendererOrders");
                    m_kernelMergeTwo          = mergeShader.FindKernel("MergeTwo");
                    m_kernelMergeTwoWithDepth = mergeShader.FindKernel("MergeTwoWithDepth");
                }
            }
            if (copyShader)
            {
                if (!copyShader.HasKernel("CopyUint4"))
                    Debug.LogError($"[GsplatSorter] CopyBufferShader '{copyShader.name}' is missing kernel 'CopyUint4' — assign GsplatCopyBuffer.compute.");
                else
                {
                    m_kernelCopyUint4 = copyShader.FindKernel("CopyUint4");
                    m_kernelCopyUint2 = copyShader.FindKernel("CopyUint2");
                }
            }

            m_globalBuffersDirty = true;
        }

        // -----------------------------------------------------------------------
        // Registration
        // -----------------------------------------------------------------------
        public void RegisterGsplat(IGsplat gsplat)
        {
            if (m_gsplats.Count == 0)
            {
                if (!GraphicsSettings.currentRenderPipeline)
                    Camera.onPreCull += OnPreCullCamera;
            }

            m_gsplats.Add(gsplat);
            m_globalBuffersDirty = true;
        }

        public void UnregisterGsplat(IGsplat gsplat)
        {
            if (!m_gsplats.Remove(gsplat))
                return;

            if (gsplat is UnityEngine.Object obj)
                m_warnedUncompressed.Remove(obj.GetInstanceID());

            m_globalBuffersDirty = true;

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
            DisposeGlobalBuffers();
        }

        // -----------------------------------------------------------------------
        // Per-frame gather
        // -----------------------------------------------------------------------
        public bool GatherGsplatsForCamera(Camera cam)
        {
            if (cam.cameraType == CameraType.Preview)
                return false;

            m_activeGsplats.Clear();
            foreach (var gs in m_gsplats.Where(gs => gs is { isActiveAndEnabled: true, Valid: true }))
                m_activeGsplats.Add(gs);

            GlobalRenderEnabled = GlobalValid
                                  && GsplatSettings.Instance.EnableGlobalSort
                                  && m_activeGsplats.Count >= 2
                                  && CanRenderGlobally();
            return m_activeGsplats.Count != 0;
        }

        // Decides scene-wide whether the global merge can run this frame. Must run before
        // DrawAllIfEnabled (which the URP feature calls in OnCameraPreCull, before DispatchSort)
        // so that the draw is correctly suppressed when global mode is unsupported.
        bool CanRenderGlobally()
        {
            // renderer_id is packed into 8 bits ([31:24]); max 255 renderers.
            if (m_activeGsplats.Count > 255)
            {
                Debug.LogError("[GsplatSorter] Global merge supports at most 255 renderers. Falling back to per-renderer rendering.");
                return false;
            }

            // Global merge requires every active renderer to use SPARK compression.
            foreach (var gs in m_activeGsplats)
            {
                if (gs.PackedSplatsBuffer == null)
                {
                    var obj = gs as UnityEngine.Object;
                    int id  = obj ? obj.GetInstanceID() : 0;
                    if (m_warnedUncompressed.Add(id))
                        Debug.LogWarning($"[GsplatSorter] '{obj?.name}' uses an uncompressed asset; global sort requires every active renderer to use SPARK compression. Disabling global sort for this scene — all renderers fall back to per-renderer rendering.");
                    return false;
                }
            }
            return true;
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

            if (GlobalRenderEnabled)
                DrawAll(m_commandBuffer, camera);
        }

        // -----------------------------------------------------------------------
        // Sort dispatch (with profiling markers)
        // -----------------------------------------------------------------------
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
            if (!GlobalRenderEnabled) return;

            cmd.BeginSample(k_samplerMerge.name);
            EnsureGlobalBuffers(cmd);
            // If EnsureGlobalBuffers failed (uncompressed asset, >255 renderers, etc.),
            // m_globalOrderBuffer is null. Disable global mode so per-renderer Render()
            // is not skipped next frame while nothing actually draws.
            if (m_globalOrderBuffer == null)
            {
                GlobalRenderEnabled = false;
                cmd.EndSample(k_samplerMerge.name);
                return;
            }
            UpdateRendererTransforms();
            DispatchMerge(cmd);
            cmd.EndSample(k_samplerMerge.name);
        }

        // -----------------------------------------------------------------------
        // Global buffer management
        // -----------------------------------------------------------------------
        void EnsureGlobalBuffers(CommandBuffer cmd)
        {
            if (!m_globalBuffersDirty) return;
            m_globalBuffersDirty = false;

            DisposeGlobalBuffers();

            m_totalSplatCount = 0;
            m_rendererOffsets = new uint[m_activeGsplats.Count];
            m_globalSHBands   = m_activeGsplats.Count > 0 ? m_activeGsplats[0].SHBands : (byte)0;

            foreach (var gs in m_activeGsplats)
                if (gs.SHBands < m_globalSHBands) m_globalSHBands = gs.SHBands;

            for (int k = 0; k < m_activeGsplats.Count; k++)
            {
                m_rendererOffsets[k] = m_totalSplatCount;
                m_totalSplatCount   += m_activeGsplats[k].SplatCount;
            }

            if (m_totalSplatCount == 0) return;

            // CanRenderGlobally() in Gather already guarded count<=255 and Spark-only;
            // GlobalRenderEnabled would be false and we wouldn't be here otherwise.

            var st = GraphicsBuffer.Target.Structured;
            m_globalPackedBuffer       = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint) * 4) { name = "Gsplat.GlobalPacked" };
            m_globalOrderBuffer        = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint))     { name = "Gsplat.GlobalOrder" };
            m_rendererOffsetsBuffer    = new GraphicsBuffer(st, m_activeGsplats.Count,  sizeof(uint))     { name = "Gsplat.RendererOffsets" };
            m_rendererTransformsBuffer = new GraphicsBuffer(st, m_activeGsplats.Count,  sizeof(float)*16) { name = "Gsplat.RendererTransforms" };

            if (m_globalSHBands >= 1)
                m_globalSH1Buffer = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint) * 2) { name = "Gsplat.GlobalSH1" };
            if (m_globalSHBands >= 2)
                m_globalSH2Buffer = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint) * 4) { name = "Gsplat.GlobalSH2" };
            if (m_globalSHBands >= 3)
                m_globalSH3Buffer = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint) * 4) { name = "Gsplat.GlobalSH3" };
            if (m_globalSHBands >= 4)
                m_globalSH4Buffer = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint) * 4) { name = "Gsplat.GlobalSH4" };

            m_rendererOffsetsBuffer.SetData(m_rendererOffsets);

            // Copy per-renderer packed splat data into global buffer sub-ranges.
            for (int k = 0; k < m_activeGsplats.Count; k++)
            {
                var gs     = m_activeGsplats[k];
                uint count = gs.SplatCount;
                uint off   = m_rendererOffsets[k];
                if (count == 0) continue;

                CopyUint4(cmd, gs.PackedSplatsBuffer, m_globalPackedBuffer, count, off);
                if (m_globalSHBands >= 1 && gs.SH1Buffer != null)
                    CopyUint2(cmd, gs.SH1Buffer, m_globalSH1Buffer, count, off);
                if (m_globalSHBands >= 2 && gs.SH2Buffer != null)
                    CopyUint4(cmd, gs.SH2Buffer, m_globalSH2Buffer, count, off);
                if (m_globalSHBands >= 3 && gs.SH3Buffer != null)
                    CopyUint4(cmd, gs.SH3Buffer, m_globalSH3Buffer, count, off);
                if (m_globalSHBands >= 4 && gs.SH4Buffer != null)
                    CopyUint4(cmd, gs.SH4Buffer, m_globalSH4Buffer, count, off);
            }

            // Allocate merge scratch buffers (always fresh after a dirty rebuild).
            m_packScratchOrders  = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint))  { name = "Gsplat.PackScratchOrders" };
            m_packScratchDepths  = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(float)) { name = "Gsplat.PackScratchDepths" };
            m_mergeScratchOrders = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint))  { name = "Gsplat.MergeScratchOrders" };
            m_mergeScratchDepths = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(float)) { name = "Gsplat.MergeScratchDepths" };
            m_scratchCapacity    = m_totalSplatCount;
        }

        void UpdateRendererTransforms()
        {
            if (m_rendererTransformsBuffer == null) return;
            int count = m_activeGsplats.Count;
            if (m_rendererTransformsCache == null || m_rendererTransformsCache.Length != count)
                m_rendererTransformsCache = new Matrix4x4[count];
            for (int k = 0; k < count; k++)
                m_rendererTransformsCache[k] = m_activeGsplats[k].transform.localToWorldMatrix;
            m_rendererTransformsBuffer.SetData(m_rendererTransformsCache);
        }

        // -----------------------------------------------------------------------
        // Copy helpers (GPU-side via command buffer)
        // -----------------------------------------------------------------------
        void CopyUint4(CommandBuffer cmd, GraphicsBuffer src, GraphicsBuffer dst, uint count, uint dstOff)
        {
            cmd.SetComputeIntParam(m_globalMaterial.CopyBufferShader, k_srcElementCount,  (int)count);
            cmd.SetComputeIntParam(m_globalMaterial.CopyBufferShader, k_dstElementOffset, (int)dstOff);
            cmd.SetComputeBufferParam(m_globalMaterial.CopyBufferShader, m_kernelCopyUint4, k_srcUint4, src);
            cmd.SetComputeBufferParam(m_globalMaterial.CopyBufferShader, m_kernelCopyUint4, k_dstUint4, dst);
            cmd.DispatchCompute(m_globalMaterial.CopyBufferShader, m_kernelCopyUint4, (int)GsplatUtils.DivRoundUp(count, 256), 1, 1);
        }

        void CopyUint2(CommandBuffer cmd, GraphicsBuffer src, GraphicsBuffer dst, uint count, uint dstOff)
        {
            cmd.SetComputeIntParam(m_globalMaterial.CopyBufferShader, k_srcElementCount,  (int)count);
            cmd.SetComputeIntParam(m_globalMaterial.CopyBufferShader, k_dstElementOffset, (int)dstOff);
            cmd.SetComputeBufferParam(m_globalMaterial.CopyBufferShader, m_kernelCopyUint2, k_srcUint2, src);
            cmd.SetComputeBufferParam(m_globalMaterial.CopyBufferShader, m_kernelCopyUint2, k_dstUint2, dst);
            cmd.DispatchCompute(m_globalMaterial.CopyBufferShader, m_kernelCopyUint2, (int)GsplatUtils.DivRoundUp(count, 256), 1, 1);
        }

        // -----------------------------------------------------------------------
        // Merge dispatch
        // -----------------------------------------------------------------------
        void DispatchMerge(CommandBuffer cmd)
        {
            int K = m_activeGsplats.Count;
            if (K < 2 || m_globalOrderBuffer == null) return;

            // Step 1: pack each renderer's sorted orders + depths into m_packScratch.
            for (int k = 0; k < K; k++)
            {
                var gs    = m_activeGsplats[k];
                if (gs.SorterResource is not Resource res) continue;
                uint cnt  = gs.RemainingCount;
                uint off  = m_rendererOffsets[k];
                if (cnt == 0) continue;

                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_rendererIdx, k);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_srcCount,    (int)cnt);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_dstOffset,   (int)off);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, m_kernelPackOrders, k_rawOrders,    res.OrderBuffer);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, m_kernelPackOrders, k_rawDepths,    res.InputKeys);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, m_kernelPackOrders, k_packedOrders, m_packScratchOrders);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, m_kernelPackOrders, k_packedDepths, m_packScratchDepths);
                cmd.DispatchCompute(m_globalMaterial.MergeShader, m_kernelPackOrders, (int)GsplatUtils.DivRoundUp(cnt, 256), 1, 1);
            }

            // Step 2: cascaded 2-way merges.
            // After each pass the merged sub-range grows. We alternate writing between
            // m_packScratch (treated as read-only source) and m_mergeScratch.
            // Source for renderer k's sorted range is always m_packScratchOrders[off[k]..off[k]+cnt[k]).
            // The merged-so-far result alternates between m_mergeScratch and a temporary swap.

            uint mergedCount  = m_activeGsplats[0].RemainingCount;   // size of merged result so far
            uint mergedOffset = 0;                                    // always at start of its scratch buffer

            // Current merged result lives in: packScratch initially (range [0, mergedCount)).
            // We swap between packScratch and mergeScratch each pass.
            bool mergedInPack = true;

            for (int k = 1; k < K; k++)
            {
                var gs      = m_activeGsplats[k];
                uint cntK   = gs.RemainingCount;
                uint offK   = m_rendererOffsets[k];
                bool isFinal = (k == K - 1);
                uint total  = mergedCount + cntK;

                // Source A: current merged result.
                var srcAOrders = mergedInPack ? m_packScratchOrders : m_mergeScratchOrders;
                var srcADepths = mergedInPack ? m_packScratchDepths : m_mergeScratchDepths;

                // Source B: renderer k's range in packScratch.
                // (always in packScratch regardless of which buffer holds merged result)
                var srcBOrders = m_packScratchOrders;
                var srcBDepths = m_packScratchDepths;

                // Destination: final pass → globalOrderBuffer; otherwise the other scratch.
                GraphicsBuffer dstOrders, dstDepths;
                if (isFinal)
                {
                    dstOrders = m_globalOrderBuffer;
                    dstDepths = null;
                }
                else
                {
                    dstOrders = mergedInPack ? m_mergeScratchOrders : m_packScratchOrders;
                    dstDepths = mergedInPack ? m_mergeScratchDepths : m_packScratchDepths;
                }

                int kernel = isFinal ? m_kernelMergeTwo : m_kernelMergeTwoWithDepth;

                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_offsetA,      (int)mergedOffset);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_offsetB,      (int)offK);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_countA,       (int)mergedCount);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_countB,       (int)cntK);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_outputOffset,  0);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_depthsA,      srcADepths);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_ordersA,      srcAOrders);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_depthsB,      srcBDepths);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_ordersB,      srcBOrders);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_outputOrders, dstOrders);
                if (!isFinal)
                    cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_outputDepths, dstDepths);

                cmd.DispatchCompute(m_globalMaterial.MergeShader, kernel, (int)GsplatUtils.DivRoundUp(total, 256), 1, 1);

                mergedCount  = total;
                mergedOffset = 0;           // output always starts at 0 in the destination buffer
                mergedInPack = !mergedInPack;
            }

            m_totalRemainingCount = mergedCount;
        }

        // -----------------------------------------------------------------------
        // Global draw call
        // -----------------------------------------------------------------------
        public void DrawAllIfEnabled(CommandBuffer cmd, Camera camera)
        {
            if (GlobalRenderEnabled)
                DrawAll(cmd, camera);
        }

        void DrawAll(CommandBuffer cmd, Camera camera)
        {
            // m_globalBuffersDirty: the active renderer set changed and the next DispatchSort
            // hasn't validated/rebuilt the buffers yet. Drawing now would bind stale buffers
            // (under URP, DrawAllIfEnabled fires before DispatchSort each frame).
            if (m_globalBuffersDirty || m_globalOrderBuffer == null || m_totalRemainingCount == 0) return;

            cmd?.BeginSample(k_samplerDraw.name);

            var mat = m_globalMaterial.Materials[m_globalSHBands];

            // Bind buffers via a MaterialPropertyBlock rather than on the material itself, so
            // each queued draw captures its own bindings and multiple cameras (Game + SceneView,
            // or any multi-camera setup) don't overwrite one another's state before the GPU
            // executes them.
            m_globalPropertyBlock ??= new MaterialPropertyBlock();
            m_globalPropertyBlock.Clear();
            m_globalPropertyBlock.SetBuffer(k_globalOrderBuffer,      m_globalOrderBuffer);
            m_globalPropertyBlock.SetBuffer(k_globalPackedBuffer,     m_globalPackedBuffer);
            m_globalPropertyBlock.SetBuffer(k_rendererOffsetsProp,    m_rendererOffsetsBuffer);
            m_globalPropertyBlock.SetBuffer(k_rendererTransformsProp, m_rendererTransformsBuffer);
            m_globalPropertyBlock.SetInteger(k_totalSplatCount,       (int)m_totalRemainingCount);
            m_globalPropertyBlock.SetInteger(k_splatInstanceSize,     (int)GsplatSettings.Instance.SplatInstanceSize);

            if (m_globalSH1Buffer != null)
                m_globalPropertyBlock.SetBuffer(k_globalSH1Buffer, m_globalSH1Buffer);
            if (m_globalSH2Buffer != null)
                m_globalPropertyBlock.SetBuffer(k_globalSH2Buffer, m_globalSH2Buffer);
            if (m_globalSH3Buffer != null)
                m_globalPropertyBlock.SetBuffer(k_globalSH3Buffer, m_globalSH3Buffer);
            if (m_globalSH4Buffer != null)
                m_globalPropertyBlock.SetBuffer(k_globalSH4Buffer, m_globalSH4Buffer);

            // Use the first renderer's visual settings as representative.
            var rep = m_activeGsplats[0] as GsplatRenderer;
            if (rep != null)
            {
                m_globalPropertyBlock.SetFloat(k_brightness,    rep.Brightness);
                m_globalPropertyBlock.SetFloat(k_scaleFactor,   1.0f - rep.SplatDownscaleFactor);
                m_globalPropertyBlock.SetInteger(k_gammaToLinear, rep.GammaToLinear ? 1 : 0);
                m_globalPropertyBlock.SetInteger(k_shDegree,      Mathf.Min(rep.SHDegree, m_globalSHBands));
            }

            var rp = new RenderParams(mat)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 1e6f),
                camera      = camera,
                matProps    = m_globalPropertyBlock
            };

            int instances = Mathf.CeilToInt(m_totalRemainingCount / (float)GsplatSettings.Instance.SplatInstanceSize);
            Graphics.RenderMeshPrimitives(rp, GsplatSettings.Instance.Mesh, 0, instances);

            cmd?.EndSample(k_samplerDraw.name);
        }

        // -----------------------------------------------------------------------
        // Cleanup
        // -----------------------------------------------------------------------
        void DisposeGlobalBuffers()
        {
            m_globalPackedBuffer?.Dispose();        m_globalPackedBuffer       = null;
            m_globalSH1Buffer?.Dispose();           m_globalSH1Buffer          = null;
            m_globalSH2Buffer?.Dispose();           m_globalSH2Buffer          = null;
            m_globalSH3Buffer?.Dispose();           m_globalSH3Buffer          = null;
            m_globalSH4Buffer?.Dispose();           m_globalSH4Buffer          = null;
            m_globalOrderBuffer?.Dispose();         m_globalOrderBuffer        = null;
            m_rendererOffsetsBuffer?.Dispose();     m_rendererOffsetsBuffer    = null;
            m_rendererTransformsBuffer?.Dispose();  m_rendererTransformsBuffer = null;
            // Scratch buffers are rebuilt every time global buffers are rebuilt,
            // so dispose them here too to avoid leaking when renderer count shrinks.
            m_packScratchOrders?.Dispose();         m_packScratchOrders        = null;
            m_packScratchDepths?.Dispose();         m_packScratchDepths        = null;
            m_mergeScratchOrders?.Dispose();        m_mergeScratchOrders       = null;
            m_mergeScratchDepths?.Dispose();        m_mergeScratchDepths       = null;
            m_scratchCapacity = 0;
        }

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------
        public ISorterResource CreateSorterResource(uint count, GraphicsBuffer orderBuffer)
        {
            return new Resource(count, orderBuffer);
        }

        /// <summary>
        /// Call when a renderer's asset changes so the global packed buffer is rebuilt next frame.
        /// </summary>
        public void MarkGlobalBuffersDirty() => m_globalBuffersDirty = true;
    }
}
