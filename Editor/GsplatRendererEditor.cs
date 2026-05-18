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
                nameof(GsplatRenderer.SHDegree),
                nameof(GsplatRenderer.AsyncUpload),
                nameof(GsplatRenderer.RenderBeforeUploadComplete),
                nameof(GsplatRenderer.Brightness),
                nameof(GsplatRenderer.SortMode)
            );

            // SH degree: slider max equals the bound asset's SHBands (so a degree-3 asset
            // shows 0–3, a degree-4 SPZ shows 0–4). Without an asset, fall back to 3.
            var rendererTarget = (GsplatRenderer)target;
            int maxShBands = rendererTarget.GsplatAsset ? rendererTarget.GsplatAsset.SHBands : 3;
            var shDegreeProp = serializedObject.FindProperty(nameof(GsplatRenderer.SHDegree));
            shDegreeProp.intValue = EditorGUILayout.IntSlider(
                "SH Degree", shDegreeProp.intValue, 0, maxShBands);

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
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.SortMode)));
            var renderer = (GsplatRenderer)target;
            // Sort Refresh Rate slider only if on correct mode
            if (renderer.SortMode == GsplatRenderer.GsplatSortMode.SortEveryNFrames ||
                renderer.SortMode == GsplatRenderer.GsplatSortMode.CutoutsEveryNSorts)
            {
                var newSortRefreshRate = (uint)EditorGUILayout.IntSlider(new GUIContent("Sort Refresh Rate"),
                    (int)renderer.SortRefreshRate, 1, 60);
                if (newSortRefreshRate != renderer.SortRefreshRate)
                {
                    renderer.SortRefreshRate = newSortRefreshRate;
                    renderer.ForceRefresh();
                }
            }

            // Cutouts Refresh Rate slider only if on correct mode
            if (renderer.SortMode == GsplatRenderer.GsplatSortMode.CutoutsEveryNSorts)
            {
                var newCutoutsRefreshRate = (uint)EditorGUILayout.IntSlider(new GUIContent("Cutouts Refresh Rate"),
                    (int)renderer.CutoutsRefreshRate, 1, 60);
                if (newCutoutsRefreshRate != renderer.CutoutsRefreshRate)
                {
                    renderer.CutoutsRefreshRate = newCutoutsRefreshRate;
                    renderer.ForceRefresh();
                }
            }
            
            var renderOrderProp = serializedObject.FindProperty(nameof(GsplatRenderer.RenderOrder));
            uint renderOrder = renderOrderProp.uintValue;

            // RenderOrder slider depend on the MaxRenderOrder setting
            if (GsplatSettings.Instance.MaxRenderOrder > 1)
                renderOrderProp.uintValue = (uint)EditorGUILayout.IntSlider(new GUIContent("Render Order"),
                    (int)renderOrder, 0, (int)GsplatSettings.Instance.MaxRenderOrder - 1);

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.AsyncUpload)));
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