// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace Gsplat
{
    [CreateAssetMenu(menuName = "Gsplat/Gsplat Material")]
    public class GsplatMaterial : ScriptableObject
    {
        public Material DefaultMaterial;
        public Material DefaultDepthMaterial;
        private Material[][] m_material;
        private Material[][] m_depthMaterial;

        public void Reset()
        {
            m_material = null;
            m_depthMaterial = null;
        }

        public Material[][] Materials // materials generated with SH bands from 0 to 4 and custom renderOrders
        {
            get
            {
                if (m_material != null && m_material[0][0] != null)
                    return m_material;

                m_material = new Material[5][];
                for (var i = 0; i < 5; ++i)
                {
                    m_material[i] = new Material[GsplatSettings.Instance.MaxRenderOrder];
                    for (var j = 0; j < GsplatSettings.Instance.MaxRenderOrder; ++j)
                    {
                        m_material[i][j] = new Material(DefaultMaterial);
                        m_material[i][j].DisableKeyword($"SH_BANDS_0");
                        m_material[i][j].DisableKeyword($"SH_BANDS_1");
                        m_material[i][j].DisableKeyword($"SH_BANDS_2");
                        m_material[i][j].DisableKeyword($"SH_BANDS_3");
                        m_material[i][j].DisableKeyword($"SH_BANDS_4");
                        m_material[i][j].EnableKeyword($"SH_BANDS_{i}");
                        m_material[i][j].renderQueue = 3000 + j;
                    }
                }
                return m_material;
            }
        }

        public Material[][] DepthMaterials
        {
            get
            {
                if (m_depthMaterial != null && m_depthMaterial[0][0] != null)
                    return m_depthMaterial;

                m_depthMaterial = new Material[5][];
                for (var i = 0; i < 5; ++i)
                {
                    m_depthMaterial[i] = new Material[GsplatSettings.Instance.MaxRenderOrder];
                    for (var j = 0; j < GsplatSettings.Instance.MaxRenderOrder; ++j)
                    {
                        m_depthMaterial[i][j] = new Material(DefaultDepthMaterial);
                        m_depthMaterial[i][j].DisableKeyword($"SH_BANDS_0");
                        m_depthMaterial[i][j].DisableKeyword($"SH_BANDS_1");
                        m_depthMaterial[i][j].DisableKeyword($"SH_BANDS_2");
                        m_depthMaterial[i][j].DisableKeyword($"SH_BANDS_3");
                        m_depthMaterial[i][j].DisableKeyword($"SH_BANDS_4");
                        m_depthMaterial[i][j].EnableKeyword($"SH_BANDS_{i}");
                        m_depthMaterial[i][j].renderQueue = 3000 + j;
                    }
                }
                return m_depthMaterial;
            }
        }

        public ComputeShader CalcDepthShader;
        public ComputeShader InitOrderShader;
    }
}
