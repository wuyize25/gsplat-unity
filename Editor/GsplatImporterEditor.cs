// Copyright (c) 2025 Niantic Spatial
// SPDX-License-Identifier: MIT

using UnityEditor;
using UnityEditor.AssetImporters;

namespace Gsplat.Editor
{
    [CustomEditor(typeof(GsplatImporter))]
    public class GsplatImporterEditor : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("Compression"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SourceCoordinates"));

            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}
