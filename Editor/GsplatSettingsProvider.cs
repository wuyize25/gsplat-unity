// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.IO;
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
            EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.Shader)));
            EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.ComputeShader)));
            EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.SplatInstanceSize)));
            EditorGUILayout.PropertyField(m_gsplatSettings.FindProperty(nameof(GsplatSettings.ShowImportErrors)));
            m_gsplatSettings.ApplyModifiedProperties();
        }

        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider()
        {
            var provider = new GsplatSettingsProvider("Project/Gsplat");
            return provider;
        }
    }
}