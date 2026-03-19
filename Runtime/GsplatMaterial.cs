// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace Gsplat
{
    [CreateAssetMenu(menuName = "Gsplat/Gsplat Material")]
    public class GsplatMaterial : ScriptableObject
    {
        public Material DefaultMaterial;
        private Material[][] m_material;
        public Material[][] Materials // materials generated with SH bands from 0 to 3 and custom renderOrders
        {
            get
            {
                if (m_material != null && m_material[0][0] != null)
                    return m_material;

                m_material = new Material[4][];
                for (var i = 0; i < 4; ++i)
                {
                    m_material[i] = new Material[GsplatSettings.Instance.MaxRenderOrder];
                    for (var j = 0; j < GsplatSettings.Instance.MaxRenderOrder; ++j)
                    {
                        m_material[i][j] = new Material(DefaultMaterial);
                        m_material[i][j].EnableKeyword($"SH_BANDS_{i}");
                        m_material[i][j].renderQueue = 3000 + j;
                    }
                }
                return m_material;
            }
        }

        public ComputeShader CalcDepthShader;
        public ComputeShader InitOrderShader;
    }
}
