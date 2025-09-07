// GPU (float key, uint payload) 8 bit-LSD radix sort, using reduce-then-scan
// Copyright Thomas Smith 2024, MIT license
// https://github.com/b0nes164/GPUSorting
// Modified by Yize Wu for Gsplat
// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Gsplat
{
    public class GsplatSortPass
    {
        static readonly int k_positionBuffer = Shader.PropertyToID("_PositionBuffer");
        static readonly int k_matrixMv = Shader.PropertyToID("_MatrixMV");
        static readonly int k_eNumKeys = Shader.PropertyToID("e_numKeys");
        static readonly int k_eThreadBlocks = Shader.PropertyToID("e_threadBlocks");
        static readonly int k_bPassHist = Shader.PropertyToID("b_passHist");
        static readonly int k_bGlobalHist = Shader.PropertyToID("b_globalHist");
        static readonly int k_eRadixShift = Shader.PropertyToID("e_radixShift");
        static readonly int k_bSort = Shader.PropertyToID("b_sort");
        static readonly int k_bSortPayload = Shader.PropertyToID("b_sortPayload");
        static readonly int k_bAlt = Shader.PropertyToID("b_alt");
        static readonly int k_bAltPayload = Shader.PropertyToID("b_altPayload");

        //The size of a threadblock partition in the sort
        const uint k_deviceRadixSortPartitionSize = 3840;

        //The size of our radix in bits
        const uint k_deviceRadixSortBits = 8;

        //Number of digits in our radix, 1 << DEVICE_RADIX_SORT_BITS
        const uint k_deviceRadixSortRadix = 256;

        //Number of sorting passes required to sort a 32bit key, KEY_BITS / DEVICE_RADIX_SORT_BITS
        const uint k_deviceRadixSortPasses = 4;

        public struct Args
        {
            public uint Count;
            public Matrix4x4 MatrixMv;
            public GraphicsBuffer PositionBuffer;
            public GraphicsBuffer InputKeys;
            public GraphicsBuffer InputValues;
            public SupportResources Resources;
        }

        public struct SupportResources
        {
            public GraphicsBuffer AltBuffer;
            public GraphicsBuffer AltPayloadBuffer;
            public GraphicsBuffer PassHistBuffer;
            public GraphicsBuffer GlobalHistBuffer;

            public static SupportResources Load(uint count)
            {
                //This is threadBlocks * DEVICE_RADIX_SORT_RADIX
                var scratchBufferSize = DivRoundUp(count, k_deviceRadixSortPartitionSize) * k_deviceRadixSortRadix;
                var reducedScratchBufferSize = k_deviceRadixSortRadix * k_deviceRadixSortPasses;

                var target = GraphicsBuffer.Target.Structured;
                var resources = new SupportResources
                {
                    AltBuffer = new GraphicsBuffer(target, (int)count, 4) { name = "DeviceRadixAlt" },
                    AltPayloadBuffer = new GraphicsBuffer(target, (int)count, 4) { name = "DeviceRadixAltPayload" },
                    PassHistBuffer = new GraphicsBuffer(target, (int)scratchBufferSize, 4)
                        { name = "DeviceRadixPassHistogram" },
                    GlobalHistBuffer = new GraphicsBuffer(target, (int)reducedScratchBufferSize, 4)
                        { name = "DeviceRadixGlobalHistogram" },
                };
                return resources;
            }

            public void Dispose()
            {
                AltBuffer?.Dispose();
                AltPayloadBuffer?.Dispose();
                PassHistBuffer?.Dispose();
                GlobalHistBuffer?.Dispose();

                AltBuffer = null;
                AltPayloadBuffer = null;
                PassHistBuffer = null;
                GlobalHistBuffer = null;
            }
        }

        readonly ComputeShader m_CS;
        readonly int m_kernelInitPayload = -1;
        readonly int m_kernelCalcDistance = -1;
        readonly int m_kernelInitDeviceRadixSort = -1;
        readonly int m_kernelUpsweep = -1;
        readonly int m_kernelScan = -1;
        readonly int m_kernelDownsweep = -1;

        readonly bool m_Valid;

        public bool Valid => m_Valid;

        public GsplatSortPass(ComputeShader cs)
        {
            m_CS = cs;
            if (cs)
            {
                m_kernelInitPayload = cs.FindKernel("InitPayload");
                m_kernelCalcDistance = cs.FindKernel("CalcDistance");
                m_kernelInitDeviceRadixSort = cs.FindKernel("InitDeviceRadixSort");
                m_kernelUpsweep = cs.FindKernel("Upsweep");
                m_kernelScan = cs.FindKernel("Scan");
                m_kernelDownsweep = cs.FindKernel("Downsweep");
            }

            m_Valid = m_kernelInitPayload >= 0 &&
                      m_kernelCalcDistance >= 0 &&
                      m_kernelInitDeviceRadixSort >= 0 &&
                      m_kernelUpsweep >= 0 &&
                      m_kernelScan >= 0 &&
                      m_kernelDownsweep >= 0;
            if (m_Valid)
            {
                if (!cs.IsSupported(m_kernelInitPayload) ||
                    !cs.IsSupported(m_kernelCalcDistance) ||
                    !cs.IsSupported(m_kernelInitDeviceRadixSort) ||
                    !cs.IsSupported(m_kernelUpsweep) ||
                    !cs.IsSupported(m_kernelScan) ||
                    !cs.IsSupported(m_kernelDownsweep))
                {
                    m_Valid = false;
                }
            }

            var ascendKeyword = new LocalKeyword(cs, "SHOULD_ASCEND");
            var sortPairKeyword = new LocalKeyword(cs, "SORT_PAIRS");
            var vulkanKeyword = new LocalKeyword(cs, "VULKAN");

            cs.EnableKeyword(ascendKeyword);
            cs.EnableKeyword(sortPairKeyword);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan)
                cs.EnableKeyword(vulkanKeyword);
            else
                cs.DisableKeyword(vulkanKeyword);
        }

        static uint DivRoundUp(uint x, uint y) => (x + y - 1) / y;

        // fill the payload buffer with 0, 1, 2, ..., count-1
        public void InitPayload(CommandBuffer cmd, GraphicsBuffer payloadBuffer, uint count)
        {
            Assert.IsTrue(Valid);
            cmd.SetComputeIntParam(m_CS, k_eNumKeys, (int)count);
            cmd.SetComputeBufferParam(m_CS, m_kernelInitPayload, k_bSortPayload, payloadBuffer);
            cmd.DispatchCompute(m_CS, m_kernelInitPayload, (int)DivRoundUp(count, 1024), 1, 1);
        }

        public void Dispatch(CommandBuffer cmd, Args args)
        {
            Assert.IsTrue(Valid);

            GraphicsBuffer positionBuffer = args.PositionBuffer;
            GraphicsBuffer srcKeyBuffer = args.InputKeys;
            GraphicsBuffer srcPayloadBuffer = args.InputValues;
            GraphicsBuffer dstKeyBuffer = args.Resources.AltBuffer;
            GraphicsBuffer dstPayloadBuffer = args.Resources.AltPayloadBuffer;

            uint numKeys = args.Count;
            uint threadBlocks = DivRoundUp(args.Count, k_deviceRadixSortPartitionSize);

            // Setup overall constants
            cmd.SetComputeIntParam(m_CS, k_eNumKeys, (int)numKeys);
            cmd.SetComputeIntParam(m_CS, k_eThreadBlocks, (int)threadBlocks);
            cmd.SetComputeMatrixParam(m_CS, k_matrixMv, args.MatrixMv);

            //CalcDistance
            cmd.SetComputeBufferParam(m_CS, m_kernelCalcDistance, k_positionBuffer, positionBuffer);
            cmd.SetComputeBufferParam(m_CS, m_kernelCalcDistance, k_bSort, srcKeyBuffer);
            cmd.SetComputeBufferParam(m_CS, m_kernelCalcDistance, k_bSortPayload, srcPayloadBuffer);
            cmd.DispatchCompute(m_CS, m_kernelCalcDistance, (int)DivRoundUp(args.Count, 1024), 1, 1);

            //Set statically located buffers
            //Upsweep
            cmd.SetComputeBufferParam(m_CS, m_kernelUpsweep, k_bPassHist, args.Resources.PassHistBuffer);
            cmd.SetComputeBufferParam(m_CS, m_kernelUpsweep, k_bGlobalHist, args.Resources.GlobalHistBuffer);

            //Scan
            cmd.SetComputeBufferParam(m_CS, m_kernelScan, k_bPassHist, args.Resources.PassHistBuffer);

            //Downsweep
            cmd.SetComputeBufferParam(m_CS, m_kernelDownsweep, k_bPassHist, args.Resources.PassHistBuffer);
            cmd.SetComputeBufferParam(m_CS, m_kernelDownsweep, k_bGlobalHist, args.Resources.GlobalHistBuffer);

            //Clear the global histogram
            cmd.SetComputeBufferParam(m_CS, m_kernelInitDeviceRadixSort, k_bGlobalHist,
                args.Resources.GlobalHistBuffer);
            cmd.DispatchCompute(m_CS, m_kernelInitDeviceRadixSort, 1, 1, 1);

            // Execute the sort algorithm in 8-bit increments
            for (uint radixShift = 0; radixShift < 32; radixShift += k_deviceRadixSortBits)
            {
                cmd.SetComputeIntParam(m_CS, k_eRadixShift, (int)radixShift);

                //Upsweep
                cmd.SetComputeBufferParam(m_CS, m_kernelUpsweep, k_bSort, srcKeyBuffer);
                cmd.DispatchCompute(m_CS, m_kernelUpsweep, (int)threadBlocks, 1, 1);

                // Scan
                cmd.DispatchCompute(m_CS, m_kernelScan, (int)k_deviceRadixSortRadix, 1, 1);

                // Downsweep
                cmd.SetComputeBufferParam(m_CS, m_kernelDownsweep, k_bSort, srcKeyBuffer);
                cmd.SetComputeBufferParam(m_CS, m_kernelDownsweep, k_bSortPayload, srcPayloadBuffer);
                cmd.SetComputeBufferParam(m_CS, m_kernelDownsweep, k_bAlt, dstKeyBuffer);
                cmd.SetComputeBufferParam(m_CS, m_kernelDownsweep, k_bAltPayload, dstPayloadBuffer);
                cmd.DispatchCompute(m_CS, m_kernelDownsweep, (int)threadBlocks, 1, 1);

                // Swap
                (srcKeyBuffer, dstKeyBuffer) = (dstKeyBuffer, srcKeyBuffer);
                (srcPayloadBuffer, dstPayloadBuffer) = (dstPayloadBuffer, srcPayloadBuffer);
            }
        }
    }
}