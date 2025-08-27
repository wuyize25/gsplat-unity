using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

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
                    AssetDatabase.CreateAsset(settings, k_gsplatSettingsPath);
                    AssetDatabase.SaveAssets();
                }
#endif

                s_instance = settings;
                return s_instance;
            }
        }

        public Shader Shader;
        public ComputeShader ComputeShader;
        public uint SplatInstanceSize = 128;
        public Material Material { get; private set; }
        public Mesh Mesh { get; private set; }

        public bool Valid => Material && Mesh && SplatInstanceSize > 0;

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
                hideFlags = HideFlags.DontSave
            };
        }

        void OnValidate()
        {
            if (SplatInstanceSize != m_prevSplatInstanceSize)
            {
                DestroyImmediate(Mesh);
                CreateMeshInstance();
                m_prevSplatInstanceSize = SplatInstanceSize;
            }

            if (Shader != m_prevShader)
            {
                DestroyImmediate(Material);
                Material = Shader ? new Material(Shader) { hideFlags = HideFlags.DontSave } : null;
                m_prevShader = Shader;
            }

            if (ComputeShader != m_prevComputeShader)
            {
                GsplatRenderSystem.Instance.InitSorter(ComputeShader);
                m_prevComputeShader = ComputeShader;
            }
        }

        void OnEnable()
        {
            CreateMeshInstance();
            m_prevSplatInstanceSize = SplatInstanceSize;

            Material = Shader ? new Material(Shader) { hideFlags = HideFlags.DontSave } : null;
            m_prevShader = Shader;
            GsplatRenderSystem.Instance.InitSorter(ComputeShader);
            m_prevComputeShader = ComputeShader;
        }
    }
}