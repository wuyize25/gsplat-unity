using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
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
        private static string ReadLine(FileStream fs)
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

        private static void ReadPlyHeader(FileStream fs, out uint vertexCount, out int propertyCount)
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

                gsplatAsset.numSplats = vertexCount;
                gsplatAsset.shBands = (byte)(Math.Sqrt((propertyCount - 14) / 3) - 1);
                gsplatAsset.positions = new Vector3[vertexCount];
                gsplatAsset.colors = new Vector4[vertexCount];
                gsplatAsset.shs = new Vector3[vertexCount * shCoeffs];
                gsplatAsset.scales = new Vector3[vertexCount];
                gsplatAsset.rotations = new Vector4[vertexCount];


                var buffer = new NativeArray<byte>(propertyCount * 4, Allocator.Temp);
                for (uint i = 0; i < vertexCount; i++)
                {
                    var readBytes = fs.Read(buffer);
                    if (readBytes != propertyCount * 4)
                        throw new IOException(
                            $"{ctx.assetPath} read error, unexpected end of file, got {readBytes} bytes at vertex {i}");
                    var properties = buffer.Reinterpret<float>(1);
                    gsplatAsset.positions[i] = new Vector3(properties[0], properties[1], properties[2]);
                    gsplatAsset.colors[i] = new Vector4(properties[6], properties[7], properties[8],
                        properties[propertyCount - 8]);
                    for (int j = 0; j < shCoeffs; j++)
                        gsplatAsset.shs[i * shCoeffs + j] = new Vector3(properties[j + 9], properties[j + 9 + shCoeffs],
                            properties[j + 9 + shCoeffs * 2]);
                    gsplatAsset.scales[i] = new Vector3(properties[propertyCount - 7], properties[propertyCount - 6],
                        properties[propertyCount - 5]);
                    gsplatAsset.rotations[i] = new Vector4(properties[propertyCount - 4], properties[propertyCount - 3],
                        properties[propertyCount - 2], properties[propertyCount - 1]);

                    if (i == 0) bounds = new Bounds(gsplatAsset.positions[i], Vector3.zero);
                    else bounds.Encapsulate(gsplatAsset.positions[i]);
                }

                buffer.Dispose();
            }

            var meshPositions = new Vector3[4 * gsplatAsset.numSplats];
            var meshIndices = new int[6 * gsplatAsset.numSplats];
            for (var i = 0; i < gsplatAsset.numSplats; ++i)
            {
                unsafe
                {
                    meshPositions[i * 4] = new Vector3(-1, -1, *(float*)&i);
                    meshPositions[i * 4 + 1] = new Vector3(1, -1, *(float*)&i);
                    meshPositions[i * 4 + 2] = new Vector3(1, 1, *(float*)&i);
                    meshPositions[i * 4 + 3] = new Vector3(-1, 1, *(float*)&i);
                }

                var b = i * 4;
                Array.Copy(new[] { 0 + b, 1 + b, 2 + b, 0 + b, 2 + b, 3 + b }, 0, meshIndices, i * 6, 6);
            }

            gsplatAsset.mesh = new Mesh
            {
                name = Path.GetFileNameWithoutExtension(ctx.assetPath),
                vertices = meshPositions,
                triangles = meshIndices,
                bounds = bounds
            };

            ctx.AddObjectToAsset("gsplatAsset", gsplatAsset);
            ctx.AddObjectToAsset("gsplatMesh", gsplatAsset.mesh);
        }
    }
}