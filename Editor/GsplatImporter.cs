// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Gsplat.Editor
{
    [ScriptedImporter(1, new[] { "ply", "spz", "sog" })]
    public class GsplatImporter : ScriptedImporter
    {
        public CompressionMode Compression = CompressionMode.Spark;

        [Tooltip("The coordinate frame the source asset was authored in.\n\n" +
                 "Positions, rotations, and SH coefficients are converted to Unity (RUF) at import time.\n\n" +
                 "RUB  — standard output of 3DGS training tools, gsplat, nerfstudio, and Niantic SPZ.\n" +
                 "RDB  — PlayCanvas SOG.\n" +
                 "RDF  — OpenCV, COLMAP camera convention.\n" +
                 "LUF  — GLB, glTF.\n" +
                 "RUF  — already in Unity space; no conversion applied.")]
        public SourceCoordinates SourceCoordinates = SourceCoordinates.Unspecified;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            bool isSpz = IsSpz(ctx.assetPath);
            bool isSog = IsSog(ctx.assetPath);
            SourceCoordinates sourceCoordinates = SourceCoordinates == SourceCoordinates.Unspecified
                ? isSog ? SourceCoordinates.RDB : SourceCoordinates.RUB
                : SourceCoordinates;
            GsplatAsset gsplatAsset = Compression switch
            {
                CompressionMode.Uncompressed => isSog
                    ? ScriptableObject.CreateInstance<GsplatAssetSogUncompressed>()
                    : isSpz
                        ? ScriptableObject.CreateInstance<GsplatAssetSpzUncompressed>()
                        : ScriptableObject.CreateInstance<GsplatAssetUncompressed>(),
                CompressionMode.Spark => isSog
                    ? ScriptableObject.CreateInstance<GsplatAssetSog>()
                    : isSpz
                        ? ScriptableObject.CreateInstance<GsplatAssetSpz>()
                        : ScriptableObject.CreateInstance<GsplatAssetSpark>(),
                _ => throw new ArgumentOutOfRangeException()
            };

#if GSPLAT_VERBOSE_IMPORT_LOGGING
            Stopwatch swTotal = Stopwatch.StartNew();
#else
            Stopwatch swTotal = null;
#endif
            SpzPhaseTimings importTimings = default;
            try
            {
                ProgressCallback progress = (info, p) => EditorUtility.DisplayProgressBar(
                    "Importing Gsplat Asset", info, p);

                if (gsplatAsset is GsplatAssetSpzUncompressed spzUncompressedAsset)
                {
                    importTimings = spzUncompressedAsset.LoadFromSpz(ctx.assetPath, sourceCoordinates, progress);
                }
                else if (gsplatAsset is GsplatAssetSpz spzAsset)
                {
                    string cachePath = GetCachePath(ctx.assetPath, Compression, sourceCoordinates);
                    if (!spzAsset.TryLoadFromCache(cachePath))
                    {
                        importTimings = spzAsset.LoadFromSpz(ctx.assetPath, sourceCoordinates, progress);
                        try
                        {
                            spzAsset.SaveToCache(cachePath);
                        }
                        catch (Exception e)
                        {
                            UnityEngine.Debug.LogWarning($"[Gsplat Import] Cache write failed: {e.Message}");
                        }
                    }
                }
                else if (gsplatAsset is GsplatAssetSogUncompressed sogUncompressedAsset)
                {
                    importTimings = sogUncompressedAsset.LoadFromSog(ctx.assetPath, sourceCoordinates, progress);
                }
                else if (gsplatAsset is GsplatAssetSog sogAsset)
                {
                    string cachePath = GetSogCachePath(ctx.assetPath, Compression, sourceCoordinates,
                        SogImageDecoder.DecoderVersion);
                    if (!sogAsset.TryLoadFromCache(cachePath))
                    {
                        importTimings = sogAsset.LoadFromSog(ctx.assetPath, sourceCoordinates, progress);
                        try
                        {
                            sogAsset.SaveToCache(cachePath);
                        }
                        catch (Exception e)
                        {
                            UnityEngine.Debug.LogWarning($"[Gsplat Import] Cache write failed: {e.Message}");
                        }
                    }
                }
                else
                {
                    gsplatAsset.LoadFromPly(ctx.assetPath, progress, sourceCoordinates);
                }
            }
            catch (Exception e)
            {
                if (GsplatSettings.Instance.ShowImportErrors)
                {
                    UnityEngine.Debug.LogError($"{ctx.assetPath} import error:");
                    UnityEngine.Debug.LogException(e);
                }

                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                swTotal?.Stop();
            }

            ctx.AddObjectToAsset("gsplatAsset", gsplatAsset);
            ctx.SetMainObject(gsplatAsset);

#if GSPLAT_VERBOSE_IMPORT_LOGGING
            LogImportStats(ctx.assetPath, gsplatAsset, isSpz || isSog, importTimings, swTotal!.ElapsedMilliseconds);
#endif
        }

        // Cache key combines filename, file size, last-write time, compression mode, and
        // source coordinate frame so any change in source file or import settings busts the cache.
        static string GetCachePath(string assetPath, CompressionMode compression, SourceCoordinates sourceCoordinates)
        {
            var fi = new FileInfo(assetPath);
            string stem = Path.GetFileNameWithoutExtension(assetPath);
            string key = $"{stem}_{fi.Length}_{fi.LastWriteTimeUtc.Ticks}_{compression}_{sourceCoordinates}";
            key = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine("Library", "GsplatCache", key + ".bin");
        }

        static string GetSogCachePath(string assetPath, CompressionMode compression, SourceCoordinates sourceCoordinates,
            string decoderVersion)
        {
            var fi = new FileInfo(assetPath);
            string stem = Path.GetFileNameWithoutExtension(assetPath);
            string ext = Path.GetExtension(assetPath).TrimStart('.').ToLowerInvariant();
            string key = $"{stem}_{ext}_{fi.Length}_{fi.LastWriteTimeUtc.Ticks}_{compression}_{sourceCoordinates}_{decoderVersion}";
            key = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine("Library", "GsplatCache", key + ".bin");
        }

        static bool IsSpz(string path) => path.EndsWith(".spz", StringComparison.OrdinalIgnoreCase);
        static bool IsSog(string path) => path.EndsWith(".sog", StringComparison.OrdinalIgnoreCase);

        static void LogImportStats(string assetPath, GsplatAsset asset, bool isSpz,
            SpzPhaseTimings spzTimings, long totalMs)
        {
            uint n = asset.SplatCount;
            long fileBytes = new FileInfo(assetPath).Length;
            double usPerSplat = n > 0 ? totalMs * 1000.0 / n : 0;
            string name = Path.GetFileName(assetPath);
            string fileMb = $"{fileBytes / 1048576.0:F2} MB";

            string phases = isSpz
                ? $" | decompress {spzTimings.DecompressMs} ms | pack {spzTimings.PackMs} ms"
                : "";

            UnityEngine.Debug.Log(
                $"[Gsplat Import] {name}: {n:N0} splats, SH bands {asset.SHBands}, {fileMb}{phases} | total {totalMs} ms ({usPerSplat:F2} µs/splat)");
        }
    }


    public class GsplatReferenceRestorer : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            var reimported = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in importedAssets)
                if (p.EndsWith(".ply", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".spz", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".sog", StringComparison.OrdinalIgnoreCase))
                    reimported.Add(p);

            if (reimported.Count == 0) return;

            var renderers = UnityEngine.Object.FindObjectsByType<GsplatRenderer>(FindObjectsSortMode.None);
            foreach (var renderer in renderers)
            {
                if (string.IsNullOrEmpty(renderer.AssetGuid)) continue;
                var path = AssetDatabase.GUIDToAssetPath(renderer.AssetGuid);
                if (string.IsNullOrEmpty(path) || !reimported.Contains(path)) continue;

                // Always reload — even if the reference is still live the GPU buffers
                // need to be refreshed whenever the asset is reimported.
                var asset = AssetDatabase.LoadAssetAtPath<GsplatAsset>(path);
                if (!asset) continue;
                renderer.GsplatAsset = asset;
                renderer.ReloadAsset();
                EditorUtility.SetDirty(renderer);
            }
        }
    }
}
