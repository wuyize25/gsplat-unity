// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEditor;
using UnityEngine;

namespace Gsplat.Editor
{
    [CustomEditor(typeof(GsplatRenderer))]
    public class GsplatRendererEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "m_Script", nameof(GsplatRenderer.UploadBatchSize),
                nameof(GsplatRenderer.RenderBeforeUploadComplete));

            var renderer = (GsplatRenderer)target;

            // Cap the SHDegree slider to the asset SHBands
            if (renderer.GsplatAsset != null && renderer.GsplatAsset.SHBands > 0)
                renderer.SHDegree = (byte)EditorGUILayout.IntSlider(new GUIContent("SH Degree"), renderer.SHDegree, 0, renderer.GsplatAsset.SHBands);
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.IntSlider(new GUIContent("SH Degree"), 0, 0, 0);
                EditorGUI.EndDisabledGroup();
            }

            if (serializedObject.FindProperty(nameof(GsplatRenderer.AsyncUpload)).boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.UploadBatchSize)));
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty(nameof(GsplatRenderer.RenderBeforeUploadComplete)));
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
