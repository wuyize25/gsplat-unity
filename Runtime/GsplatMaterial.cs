using UnityEngine;

namespace Gsplat
{
    [CreateAssetMenu(menuName = "Gsplat/Gsplat Material")]
    public class GsplatMaterial : ScriptableObject
    {
        public Material[] Materials; // materials for SH bands from 0 to 3
        public ComputeShader CalcDepthShader;
    }
}