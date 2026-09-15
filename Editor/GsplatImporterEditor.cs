// Copyright (c) 2025 Niantic Spatial
// SPDX-License-Identifier: MIT

using System.IO;
using System.Linq;
using UnityEngine;
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

            if (targets.OfType<GsplatImporter>().All(imp => Path.GetExtension(imp.assetPath).ToLowerInvariant() == ".ply"))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OpacityPruneThreshold"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ColorScale"));
                if (GUILayout.Button("Open Mesh Generator"))
                {
                    var firstImporter = targets[0] as GsplatImporter;
                    GsplatMeshGenerator.ShowWindowWithFile(firstImporter.assetPath);
                }
            }

            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}