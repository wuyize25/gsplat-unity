// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Gsplat.Editor
{
    [ScriptedImporter(1, "ply")]
    public class GsplatImporter : ScriptedImporter
    {
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

        public static void ReadPlyHeader(FileStream fs, out uint vertexCount, out int propertyCount)
        {
            vertexCount = 0;
            propertyCount = 0;

            string line;
            while ((line = ReadLine(fs)) != null && line != "end_header")
            {
                string[] tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    vertexCount = uint.Parse(tokens[2]);
                if (tokens.Length == 3 && tokens[0] == "property")
                    propertyCount++;
            }
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
        }

        public static PlyHeaderInfo ReadPlyHeader(FileStream fs)
        {
            var info = new PlyHeaderInfo();

            while (ReadLine(fs) is { } line && line != "end_header")
            {
                var tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    info.VertexCount = uint.Parse(tokens[2]);
                if (tokens.Length != 3 || tokens[0] != "property") continue;
                switch (tokens[2])
                {
                    case "x":
                        info.PositionOffset = info.PropertyCount;
                        break;
                    case "f_dc_0":
                        info.ColorOffset = info.PropertyCount;
                        break;
                    case "f_rest_0":
                        info.SHOffset = info.PropertyCount;
                        break;
                    case "opacity":
                        info.OpacityOffset = info.PropertyCount;
                        break;
                    case "scale_0":
                        info.ScaleOffset = info.PropertyCount;
                        break;
                    case "rot_0":
                        info.RotationOffset = info.PropertyCount;
                        break;
                }

                if (tokens[2].StartsWith("f_rest_"))
                    info.SHPropertyCount++;
                info.PropertyCount++;
            }

            return info;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var gsplatAsset = ScriptableObject.CreateInstance<GsplatAsset>();
            var bounds = new Bounds();

            using (var fs = new FileStream(ctx.assetPath, FileMode.Open, FileAccess.Read))
            {
                // C# arrays and NativeArrays make it hard to have a "byte" array larger than 2GB :/
                if (fs.Length >= 2 * 1024 * 1024 * 1024L)
                {
                    if (GsplatSettings.Instance.ShowImportErrors)
                        Debug.LogError(
                            $"{ctx.assetPath} import error: currently files larger than 2GB are not supported");
                    return;
                }

                var plyInfo = ReadPlyHeader(fs);
                var shCoeffs = plyInfo.SHPropertyCount / 3;
                gsplatAsset.SplatCount = plyInfo.VertexCount;
                gsplatAsset.SHBands = GsplatUtils.CalcSHBandsFromSHPropertyCount(plyInfo.SHPropertyCount);

                if (gsplatAsset.SHBands > 3 ||
                    GsplatUtils.SHBandsToCoefficientCount(gsplatAsset.SHBands) * 3 != plyInfo.SHPropertyCount)
                {
                    if (GsplatSettings.Instance.ShowImportErrors)
                        Debug.LogError(
                            $"{ctx.assetPath} import error: unexpected SH property count {plyInfo.SHPropertyCount}");
                    return;
                }

                if (plyInfo.PositionOffset == -1 || plyInfo.ColorOffset == -1 || plyInfo.OpacityOffset == -1 ||
                    plyInfo.ScaleOffset == -1 || plyInfo.RotationOffset == -1)
                {
                    if (GsplatSettings.Instance.ShowImportErrors)
                        Debug.LogError(
                            $"{ctx.assetPath} import error: missing required properties in PLY header");
                    return;
                }

                gsplatAsset.Positions = new Vector3[plyInfo.VertexCount];
                if (shCoeffs > 0)
                    gsplatAsset.SHs = new Vector3[plyInfo.VertexCount * shCoeffs];
                gsplatAsset.PackedSplats = new uint[plyInfo.VertexCount * 4];

                var buffer = new byte[plyInfo.PropertyCount * sizeof(float)];
                for (uint i = 0; i < plyInfo.VertexCount; i++)
                {
                    var readBytes = fs.Read(buffer);
                    if (readBytes != buffer.Length)
                    {
                        if (GsplatSettings.Instance.ShowImportErrors)
                            Debug.LogError(
                                $"{ctx.assetPath} import error: unexpected end of file, got {readBytes} bytes at vertex {i}");
                        return;
                    }

                    var properties = MemoryMarshal.Cast<byte, float>(buffer);
                    gsplatAsset.Positions[i] = new Vector3(
                        properties[plyInfo.PositionOffset],
                        properties[plyInfo.PositionOffset + 1],
                        properties[plyInfo.PositionOffset + 2]);
                    for (int j = 0; j < shCoeffs; j++)
                        gsplatAsset.SHs[i * shCoeffs + j] = new Vector3(
                            properties[j + plyInfo.SHOffset],
                            properties[j + plyInfo.SHOffset + shCoeffs],
                            properties[j + plyInfo.SHOffset + shCoeffs * 2]);

                    if (i == 0) bounds = new Bounds(gsplatAsset.Positions[i], Vector3.zero);
                    else bounds.Encapsulate(gsplatAsset.Positions[i]);

                    var color = new Vector4(
                        properties[plyInfo.ColorOffset],
                        properties[plyInfo.ColorOffset + 1],
                        properties[plyInfo.ColorOffset + 2],
                        properties[plyInfo.OpacityOffset]);

                    var position = new Vector3(
                        properties[plyInfo.PositionOffset],
                        properties[plyInfo.PositionOffset + 1],
                        properties[plyInfo.PositionOffset + 2]);

                    var scale = new Vector3(
                        properties[plyInfo.ScaleOffset],
                        properties[plyInfo.ScaleOffset + 1],
                        properties[plyInfo.ScaleOffset + 2]);

                    var rotation = new Vector4(properties[plyInfo.RotationOffset],
                        properties[plyInfo.RotationOffset + 1],
                        properties[plyInfo.RotationOffset + 2],
                        properties[plyInfo.RotationOffset + 3]).normalized;

                    uint[] packedSplat = GsplatPacker.PackSplat(color, position, scale, rotation);

                    Array.Copy(packedSplat, 0, gsplatAsset.PackedSplats, i * 4, 4);

                    EditorUtility.DisplayProgressBar("Importing Gsplat Asset", "Reading vertices",
                        i / (float)plyInfo.VertexCount);
                }
            }

            gsplatAsset.Bounds = bounds;
            ctx.AddObjectToAsset("gsplatAsset", gsplatAsset);
        }
    }
}
