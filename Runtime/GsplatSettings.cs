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
                    settings.Reset();
                    AssetDatabase.CreateAsset(settings, k_gsplatSettingsPath);
                    AssetDatabase.SaveAssets();
                }
                else if (settings.Version < new Version("1.2.0"))
                {
                    Debug.Log($"Updated GsplatSettings from version {settings.Version}.");
                    settings.Materials = DefaultMaterials;
                    settings.m_prevComputeShader = null;
                    settings.Version = GsplatUtils.k_Version;
                    settings.OnValidate();
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }
#endif

                s_instance = settings;
                return s_instance;
            }
        }

        public ComputeShader ComputeShader;
        public uint SplatInstanceSize = 128;
        public uint UploadBatchSize = 100000;
        public bool ShowImportErrors = true;
        public GsplatMaterial[] Materials;
        public Mesh Mesh { get; private set; }
        public bool DisplayGSplatsBoundingBoxes = false;
        [Range(1, 20)] public uint MaxRenderOrder = 1;

        public bool Valid => Materials?.Length != 0 && Mesh && SplatInstanceSize > 0;

        public Version Version
        {
            get => Version.Parse(m_version);
            set => m_version = value.ToString();
        }

        ComputeShader m_prevComputeShader;
        uint m_prevSplatInstanceSize;

        [HideInInspector] [SerializeField] string m_version = "1.0.0";

#if UNITY_EDITOR
        static ComputeShader DefaultComputeShader => AssetDatabase.LoadAssetAtPath<ComputeShader>(
            GsplatUtils.k_PackagePath +
            "Runtime/Shaders/Gsplat.compute");

        static GsplatMaterial[] DefaultMaterials
        {
            get
            {
                var materials = new GsplatMaterial[Enum.GetValues(typeof(CompressionMode)).Length];
                materials[(int)CompressionMode.Uncompressed] =
                    AssetDatabase.LoadAssetAtPath<GsplatMaterial>(GsplatUtils.k_PackagePath +
                                                                  "Runtime/Materials/GsplatUncompressed.asset");
                materials[(int)CompressionMode.Spark] =
                    AssetDatabase.LoadAssetAtPath<GsplatMaterial>(GsplatUtils.k_PackagePath +
                                                                  "Runtime/Materials/GsplatSpark.asset");
                return materials;
            }
        }

        public void Reset()
        {
            Version = GsplatUtils.k_Version;
            ComputeShader = DefaultComputeShader;
            Materials = DefaultMaterials;
            m_prevComputeShader = null;
            m_prevSplatInstanceSize = 0;
            OnValidate();
        }
#endif

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
#if UNITY_EDITOR
            foreach (var mat in Materials)
            {
                mat.Reset();
            }
#endif
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
