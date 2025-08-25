using System.ComponentModel;
using UnityEngine;

namespace Gsplat
{
    public class GsplatAsset : ScriptableObject
    {
        public uint numSplats;
        public byte shBands; // 0, 1, 2, or 3
        [HideInInspector] public Vector3[] positions;
        [HideInInspector] public Vector4[] colors; // RGBA
        [HideInInspector] public Vector3[] shs;
        [HideInInspector] public Vector3[] scales;
        [HideInInspector] public Vector4[] rotations;
        public Mesh mesh;
    }
}