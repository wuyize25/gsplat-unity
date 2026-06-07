// Copyright (c) 2026 Keir Rice
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace Gsplat
{
    [CreateAssetMenu(menuName = "Gsplat/Gsplat Global Material")]
    public class GsplatGlobalMaterial : ScriptableObject
    {
        public Material DefaultMaterial;
        public ComputeShader MergeShader;
        public ComputeShader CopyBufferShader;

        Material[] m_materials; // indexed by SH band (0–4)

        /// <summary>
        /// Returns five Material instances — one per SH band level — with the appropriate
        /// SH_BANDS_N keyword pre-enabled. Lazily allocated and cached.
        /// </summary>
        public Material[] Materials
        {
            get
            {
                if (m_materials != null && m_materials[0] != null)
                    return m_materials;

                m_materials = new Material[5];
                for (int i = 0; i < 5; i++)
                {
                    m_materials[i] = new Material(DefaultMaterial);
                    m_materials[i].DisableKeyword("SH_BANDS_0");
                    m_materials[i].DisableKeyword("SH_BANDS_1");
                    m_materials[i].DisableKeyword("SH_BANDS_2");
                    m_materials[i].DisableKeyword("SH_BANDS_3");
                    m_materials[i].DisableKeyword("SH_BANDS_4");
                    m_materials[i].EnableKeyword($"SH_BANDS_{i}");
                }

                return m_materials;
            }
        }

        public void Reset() => m_materials = null;

        public bool Valid() => MergeShader && CopyBufferShader && DefaultMaterial;
    }
}