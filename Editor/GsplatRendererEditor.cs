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

            DrawPropertiesExcluding(serializedObject, "m_Script",
                nameof(GsplatRenderer.AsyncUpload),
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

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.AsyncUpload)));
            
            var renderOrderProp = serializedObject.FindProperty(nameof(GsplatRenderer.RenderOrder));
            uint renderOrder = renderOrderProp.uintValue;

            // RenderOrder slider depend on the MaxRenderOrder setting
            if (GsplatSettings.Instance.MaxRenderOrder > 1)
                renderOrderProp.uintValue = (uint)EditorGUILayout.IntSlider(new GUIContent("Render Order"), (int)renderOrder, 0, (int)GsplatSettings.Instance.MaxRenderOrder - 1);

            if (serializedObject.FindProperty(nameof(GsplatRenderer.AsyncUpload)).boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty(nameof(GsplatRenderer.RenderBeforeUploadComplete)));
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
