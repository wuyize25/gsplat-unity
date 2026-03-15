// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEditor;

namespace Gsplat.Editor
{
    [CustomEditor(typeof(GsplatRenderer))]
    public class GsplatRendererEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "m_Script",
                nameof(GsplatRenderer.UploadBatchSize),
                nameof(GsplatRenderer.RenderBeforeUploadComplete),
                nameof(GsplatRenderer.Brightness)
            );

            var brightnessProp = serializedObject.FindProperty(nameof(GsplatRenderer.Brightness));
            float brightness = brightnessProp.floatValue;
            
            // Use log scale for the slider UX
            // range from -3 (~5%) to 3 (~20x)
            float logVal = UnityEngine.Mathf.Log(
                UnityEngine.Mathf.Max(0.001f, brightness)
            );
            logVal = EditorGUILayout.Slider("Log Brightness", logVal, -4.0f, 3.0f);
            brightness = EditorGUILayout.FloatField(
                "Brightness", UnityEngine.Mathf.Exp(logVal)
            );
            brightnessProp.floatValue = brightness;

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