// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Editor
{
    public class GsplatSettingsProvider : SettingsProvider
    {
        SerializedObject m_gsplatSettings;

        public GsplatSettingsProvider(string path, SettingsScope scopes = SettingsScope.Project,
            IEnumerable<string> keywords = null) : base(path, scopes, keywords)
        {
        }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            m_gsplatSettings = new SerializedObject(GsplatSettings.Instance);
        }

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.ComputeShader)));
            EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.SplatInstanceSize)));
            EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.UploadBatchSize)));
            EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.MaxRenderOrder)));
            EditorGUILayout.PropertyField(
                m_gsplatSettings.FindProperty(nameof(GsplatSettings.CameraTranslationRefreshTreshold)));
            EditorGUILayout.PropertyField(
                m_gsplatSettings.FindProperty(nameof(GsplatSettings.CameraRotationRefreshTreshold)));
            EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.DisplayBoundingBoxes)));
            EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.ShowImportErrors)));
            EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.Materials)));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Global Sort", EditorStyles.boldLabel);
            var enableGlobalSortProp = m_gsplatSettings.FindProperty(nameof(GsplatSettings.EnableGlobalSort));
            EditorGUILayout.PropertyField(enableGlobalSortProp);
            if (enableGlobalSortProp.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Only Spark-compressed assets participate in the unified depth sort. " +
                    "Renderers using uncompressed assets fall back to per-renderer sorting and " +
                    "may not interleave correctly with the merged draw.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.GlobalMaterial)));
            }

            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset", GUILayout.Width(60)))
                ResetToDefaults();
            GUILayout.EndHorizontal();
            m_gsplatSettings.ApplyModifiedProperties();
        }

        void ResetToDefaults()
        {
            Undo.RecordObject(GsplatSettings.Instance, "Reset Gsplat Settings");
            GsplatSettings.Instance.Reset();
            EditorUtility.SetDirty(GsplatSettings.Instance);
            m_gsplatSettings = new SerializedObject(GsplatSettings.Instance);
        }

        [SettingsProvider]
        public static SettingsProvider CreateGsplatSettingsProvider()
        {
            var provider = new GsplatSettingsProvider("Project/Gsplat");
            return provider;
        }
    }
}