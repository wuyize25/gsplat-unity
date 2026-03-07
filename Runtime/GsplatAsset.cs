// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public enum CompressionMode
    {
        Uncompressed,
        Spark
    }

    public class PlyHeaderInfo
    {
        public uint VertexCount = 0;
        public int PropertyCount = 0;
        public int SHPropertyCount = 0;
        public int PositionOffset = -1;
        public int ColorOffset = -1;
        public int SHOffset = -1;
        public int OpacityOffset = -1;
        public int ScaleOffset = -1;
        public int RotationOffset = -1;

        /// <summary>
        /// Read each line, used for header reading.
        /// </summary>
        /// <param name="fs"></param>
        /// <returns></returns>
        static string ReadLine(FileStream fs)
        {
            List<byte> byteBuffer = new List<byte>();
            while (true)
            {
                int b = fs.ReadByte();
                if (b == -1 || b == '\n') break;
                byteBuffer.Add((byte)b);
            }

            // If line had CRLF line endings, remove the CR part
            if (byteBuffer.Count > 0 && byteBuffer.Last() == '\r')
            {
                byteBuffer.RemoveAt(byteBuffer.Count - 1);
            }

            return Encoding.UTF8.GetString(byteBuffer.ToArray());
        }

        public PlyHeaderInfo(FileStream fs)
        {
            while (ReadLine(fs) is { } line && line != "end_header")
            {
                var tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    VertexCount = uint.Parse(tokens[2]);
                if (tokens.Length != 3 || tokens[0] != "property") continue;
                switch (tokens[2])
                {
                    case "x":
                        PositionOffset = PropertyCount;
                        break;
                    case "f_dc_0":
                        ColorOffset = PropertyCount;
                        break;
                    case "f_rest_0":
                        SHOffset = PropertyCount;
                        break;
                    case "opacity":
                        OpacityOffset = PropertyCount;
                        break;
                    case "scale_0":
                        ScaleOffset = PropertyCount;
                        break;
                    case "rot_0":
                        RotationOffset = PropertyCount;
                        break;
                }

                if (tokens[2].StartsWith("f_rest_"))
                    SHPropertyCount++;
                PropertyCount++;
            }
        }
    }

    public delegate void ProgressCallback(string info, float progress);

    public abstract class GsplatAsset : ScriptableObject
    {
        public uint SplatCount;
        public byte SHBands; // 0, 1, 2, or 3
        public Bounds Bounds;
        public GsplatMaterial GsplatMaterial;
        public abstract CompressionMode Compression { get; }

        public bool Uploaded { get; private set; }
        public uint UploadedCount { get; protected set; }
        public Material Material => GsplatMaterial.Materials[SHBands];

        bool m_allocatedGPU;

        void OnDisable()
        {
            ReleaseGPU();
            m_allocatedGPU = false;
            Uploaded = false;
        }

        public abstract void Allocate();
        public abstract void LoadFromPly(string plyPath, ProgressCallback progressCallback = null);

        void EnsureGPUResources()
        {
            if (m_allocatedGPU) return;
            AllocateGPU();
            Uploaded = false;
            UploadedCount = 0;
            m_allocatedGPU = true;
        }

        protected abstract void AllocateGPU();
        protected abstract void ReleaseGPU();

        public void UploadData()
        {
            EnsureGPUResources();
            if (Uploaded) return;
            _UploadData();
            Uploaded = true;
            UploadedCount = SplatCount;
        }

        public Task UploadDataAsync()
        {
            EnsureGPUResources();
            if (Uploaded) return Task.CompletedTask;
            Uploaded = true;
            return _UploadDataAsync();
        }

        protected abstract Task _UploadDataAsync();

        protected abstract void _UploadData();
        public abstract void SetupMaterialPropertyBlock(MaterialPropertyBlock propertyBlock);

        public abstract void ComputeDepth(GsplatMaterial material, CommandBuffer cmd, Matrix4x4 matrixMv,
            ISorterResource sorterResource);
    }
}