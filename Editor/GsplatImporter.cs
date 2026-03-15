// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Gsplat.Editor
{
    [ScriptedImporter(1, "ply")]
    public class GsplatImporter : ScriptedImporter
    {
        public CompressionMode Compression = CompressionMode.Spark;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            GsplatAsset gsplatAsset = Compression switch
            {
                CompressionMode.Uncompressed => ScriptableObject.CreateInstance<GsplatAssetUncompressed>(),
                CompressionMode.Spark => ScriptableObject.CreateInstance<GsplatAssetSpark>(),
                _ => throw new ArgumentOutOfRangeException()
            };

            try
            {
                gsplatAsset.LoadFromPly(ctx.assetPath, (info, progress) => EditorUtility.DisplayProgressBar(
                    "Importing Gsplat Asset", info, progress));
            }
            catch (Exception e)
            {
                if (GsplatSettings.Instance.ShowImportErrors)
                    Debug.LogError($"{ctx.assetPath} import error: {e.Message}");
                return;
            }

            ctx.AddObjectToAsset("gsplatAsset", gsplatAsset);
        }
    }
}