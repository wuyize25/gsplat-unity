// Originated from aras-p/UnityGaussianSplatting by Aras Pranckevičius
// https://github.com/aras-p/UnityGaussianSplatting/blob/main/package/Runtime/GaussianCutout.cs
// Copyright (c) 2023 Aras Pranckevičius
// Modified by Arthur Aillet
// Copyright (c) 2026 Arthur Aillet
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

namespace Gsplat
{
    [ExecuteInEditMode]
    public class GsplatCutout : MonoBehaviour
    {
        public enum Type
        {
            Ellipsoid,
            Box
        }

        public enum Target
        {
            Parent,
            All,
            Specific
        }

        public bool m_Invert = false;
        public Type m_Type = Type.Ellipsoid;
        public Target m_Target = Target.Parent;
        [HideInInspector] public GsplatRenderer m_SpecifcRenderer;

        public static int ShaderDataSize => UnsafeUtility.SizeOf<ShaderData>();

        public struct ShaderData
        {
            public Matrix4x4 matrix;
            public uint typeAndFlags;
        }

        public static List<GsplatCutout> m_RegisteredCutouts = new() { };

        void OnEnable()
        {
            if (!m_RegisteredCutouts.Contains(this))
            {
                m_RegisteredCutouts.Add(this);
            }
        }

        void OnDisable()
        {
            if (m_RegisteredCutouts.Contains(this))
            {
                m_RegisteredCutouts.Remove(this);
            }
        }

        public ShaderData GetShaderData(Matrix4x4 rendererMatrix)
        {
            ShaderData sd = default;
            if (isActiveAndEnabled)
            {
                sd.matrix = transform.worldToLocalMatrix * rendererMatrix;
                sd.typeAndFlags = ((uint)m_Type) | (m_Invert ? 0x100u : 0u);
            }
            else
            {
                sd.typeAndFlags = ~0u;
            }

            return sd;
        }

#if UNITY_EDITOR
        public void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Color color;
            if (m_Target == Target.Parent)
            {
                if (transform.parent?.GetComponent<GsplatRenderer>() == null)
                    color = Color.red;
                else
                    color = Color.magenta;
            }
            else if (m_Target == Target.All)
                color = new Color(1f, 0.6470588f, 0f, 1f);
            else
            {
                if (m_SpecifcRenderer == null)
                    color = Color.red;
                else
                    color = Color.cyan;
            }

            color.a = 0.2f;
            if (Selection.Contains(gameObject))
                color.a = 0.9f;
            else
            {
                // mid amount of alpha if a GS object that contains us as a cutout is selected
                var activeGo = Selection.activeGameObject;
                if (activeGo != null)
                {
                    if (activeGo.TryGetComponent<GsplatRenderer>(out var activeSplat))
                    {
                        if (activeSplat.transform == transform.parent)
                            color.a = 0.5f;
                    }
                }
            }

            Gizmos.color = color;
            if (m_Type == Type.Ellipsoid)
            {
                Gizmos.DrawWireSphere(Vector3.zero, 1.0f);
            }

            if (m_Type == Type.Box)
            {
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one * 2);
            }
        }
#endif // #if UNITY_EDITOR
    }
}
