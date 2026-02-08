// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gsplat
{
    public class GsplatSettings : ScriptableObject
    {
        const string k_gsplatSettingsResourcesPath = "GsplatSettings";

        const string k_gsplatSettingsPath =
            "Assets/Gsplat/Settings/Resources/" + k_gsplatSettingsResourcesPath + ".asset";

        static GsplatSettings s_instance;

        public static GsplatSettings Instance
        {
            get
            {
                if (s_instance)
                    return s_instance;

                var settings = Resources.Load<GsplatSettings>(k_gsplatSettingsResourcesPath);
#if UNITY_EDITOR
                if (!settings)
                {
                    var assetPath = Path.GetDirectoryName(k_gsplatSettingsPath);
                    if (!Directory.Exists(assetPath))
                        Directory.CreateDirectory(assetPath);

                    settings = CreateInstance<GsplatSettings>();
                    settings.ComputeShader =
                        AssetDatabase.LoadAssetAtPath<ComputeShader>(GsplatUtils.k_PackagePath +
                                                                     "Runtime/Shaders/Gsplat.compute");
                    settings.CalcDepthShader =
                        AssetDatabase.LoadAssetAtPath<ComputeShader>(GsplatUtils.k_PackagePath +
                                                                     "Runtime/Shaders/CalcDepth.compute");
                    settings.CalcDepthSparkShader =
                        AssetDatabase.LoadAssetAtPath<ComputeShader>(GsplatUtils.k_PackagePath +
                                                                     "Runtime/Shaders/CalcDepthSpark.compute");
                    settings.Materials = new GsplatMaterial[Enum.GetValues(typeof(CompressionMode)).Length];
                    settings.Materials[(int)CompressionMode.Uncompressed] =
                        AssetDatabase.LoadAssetAtPath<GsplatMaterial>(GsplatUtils.k_PackagePath +
                                                                      "Runtime/Materials/GsplatUncompressed.asset");
                    settings.Materials[(int)CompressionMode.Spark] =
                        AssetDatabase.LoadAssetAtPath<GsplatMaterial>(GsplatUtils.k_PackagePath +
                                                                      "Runtime/Materials/GsplatSpark.asset");

                    settings.OnValidate();
                    AssetDatabase.CreateAsset(settings, k_gsplatSettingsPath);
                    AssetDatabase.SaveAssets();
                }
#endif

                s_instance = settings;
                return s_instance;
            }
        }

        public ComputeShader ComputeShader;
        public ComputeShader CalcDepthShader;
        public ComputeShader CalcDepthSparkShader;
        public uint SplatInstanceSize = 128;
        public bool ShowImportErrors = true;
        public GsplatMaterial[] Materials; // { get; private set; }
        public Mesh Mesh { get; private set; }

        public bool Valid => Materials?.Length != 0 && Mesh && SplatInstanceSize > 0;

        Shader m_prevShader;
        ComputeShader m_prevComputeShader;
        uint m_prevSplatInstanceSize;

        void CreateMeshInstance()
        {
            var meshPositions = new Vector3[4 * SplatInstanceSize];
            var meshIndices = new int[6 * SplatInstanceSize];
            for (uint i = 0; i < SplatInstanceSize; ++i)
            {
                unsafe
                {
                    meshPositions[i * 4] = new Vector3(-1, -1, *(float*)&i);
                    meshPositions[i * 4 + 1] = new Vector3(1, -1, *(float*)&i);
                    meshPositions[i * 4 + 2] = new Vector3(-1, 1, *(float*)&i);
                    meshPositions[i * 4 + 3] = new Vector3(1, 1, *(float*)&i);
                }

                int b = (int)i * 4;
                Array.Copy(new[] { 0 + b, 1 + b, 2 + b, 1 + b, 3 + b, 2 + b }, 0, meshIndices, i * 6, 6);
            }

            Mesh = new Mesh
            {
                name = "GsplatMeshInstance",
                vertices = meshPositions,
                triangles = meshIndices,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        /*void CreateMaterials()
        {
            if (Materials != null)
                foreach (var mat in Materials)
                    DestroyImmediate(mat);

            if (!Shader)
            {
                Materials = null;
                return;
            }

            Materials = new Material[4];
            for (var i = 0; i < 4; ++i)
            {
                Materials[i] = new Material(Shader)
                {
                    /*hideFlags = HideFlags.HideAndDontSave#1#
                };
                Materials[i].EnableKeyword($"SH_BANDS_{i}");
                Materials[i].EnableKeyword("SPARK");
                AssetDatabase.CreateAsset(Materials[i], GsplatUtils.k_PackagePath +
                                                        $"Runtime/Materials/GsplatSpark{i}.mat");
                AssetDatabase.SaveAssets();
            }
        }*/

        void OnValidate()
        {
            if (ComputeShader != m_prevComputeShader)
            {
                GsplatSorter.Instance.InitSorter(ComputeShader);
                m_prevComputeShader = ComputeShader;
            }

            if (SplatInstanceSize != m_prevSplatInstanceSize)
            {
                DestroyImmediate(Mesh);
                CreateMeshInstance();
                m_prevSplatInstanceSize = SplatInstanceSize;
            }
        }

        void OnEnable()
        {
            GsplatSorter.Instance.InitSorter(ComputeShader);
            m_prevComputeShader = ComputeShader;

            CreateMeshInstance();
            m_prevSplatInstanceSize = SplatInstanceSize;
        }
    }
}