using System.ComponentModel;
using UnityEngine;
using UnityEngine.Serialization;

namespace Gsplat
{
    public class GsplatAsset : ScriptableObject
    {
        [FormerlySerializedAs("NumSplats")] public uint SplatCount;
        public byte SHBands; // 0, 1, 2, or 3
        [HideInInspector] public Vector3[] Positions;
        [HideInInspector] public Vector4[] Colors; // RGB, Opacity
        [HideInInspector] public Vector3[] Shs;
        [HideInInspector] public Vector3[] Scales;
        [HideInInspector] public Vector4[] Rotations;
        public Bounds Bounds;
    }
}