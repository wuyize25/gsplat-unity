using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

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

        static void ReadPlyHeader(FileStream fs, out uint vertexCount, out int propertyCount)
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

        static float Sigmoid(float x)
        {
            return 1.0f / (1.0f + Mathf.Exp(-x));
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var gsplatAsset = ScriptableObject.CreateInstance<GsplatAsset>();
            var bounds = new Bounds();

            using (var fs = new FileStream(ctx.assetPath, FileMode.Open, FileAccess.Read))
            {
                // C# arrays and NativeArrays make it hard to have a "byte" array larger than 2GB :/
                if (fs.Length >= 2 * 1024 * 1024 * 1024L)
                    throw new IOException(
                        $"{ctx.assetPath} read error: currently files larger than 2GB are not supported");

                ReadPlyHeader(fs, out uint vertexCount, out int propertyCount);

                var shCoeffs = (propertyCount - 17) / 3;

                gsplatAsset.SplatCount = vertexCount;
                gsplatAsset.SHBands = (byte)(Math.Sqrt((propertyCount - 14) / 3) - 1);
                gsplatAsset.Positions = new Vector3[vertexCount];
                gsplatAsset.Colors = new Vector4[vertexCount];
                if (shCoeffs > 0)
                    gsplatAsset.SHs = new Vector3[vertexCount * shCoeffs];
                gsplatAsset.Scales = new Vector3[vertexCount];
                gsplatAsset.Rotations = new Vector4[vertexCount];


                var buffer = new NativeArray<byte>(propertyCount * 4, Allocator.Temp);
                for (uint i = 0; i < vertexCount; i++)
                {
                    var readBytes = fs.Read(buffer);
                    if (readBytes != propertyCount * 4)
                        throw new IOException(
                            $"{ctx.assetPath} read error, unexpected end of file, got {readBytes} bytes at vertex {i}");
                    var properties = buffer.Reinterpret<float>(1);
                    gsplatAsset.Positions[i] = new Vector3(properties[0], properties[1], properties[2]);
                    gsplatAsset.Colors[i] = new Vector4(properties[6], properties[7], properties[8],
                        Sigmoid(properties[propertyCount - 8]));
                    for (int j = 0; j < shCoeffs; j++)
                        gsplatAsset.SHs[i * shCoeffs + j] = new Vector3(properties[j + 9], properties[j + 9 + shCoeffs],
                            properties[j + 9 + shCoeffs * 2]);
                    gsplatAsset.Scales[i] = new Vector3(Mathf.Exp(properties[propertyCount - 7]),
                        Mathf.Exp(properties[propertyCount - 6]),
                        Mathf.Exp(properties[propertyCount - 5]));
                    gsplatAsset.Rotations[i] = new Vector4(properties[propertyCount - 4], properties[propertyCount - 3],
                        properties[propertyCount - 2], properties[propertyCount - 1]).normalized;

                    if (i == 0) bounds = new Bounds(gsplatAsset.Positions[i], Vector3.zero);
                    else bounds.Encapsulate(gsplatAsset.Positions[i]);
                }

                buffer.Dispose();
            }

            gsplatAsset.Bounds = bounds;
            ctx.AddObjectToAsset("gsplatAsset", gsplatAsset);
        }
    }
}