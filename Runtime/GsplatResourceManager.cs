using System.Collections.Generic;
using UnityEngine;

namespace Gsplat
{
    public static class GsplatResourceManager
    {
        class Cache
        {
            public GsplatResource Resource;
            public int RefCount;
        }

        static readonly Dictionary<int, Cache> k_resourceCache = new();

        public static GsplatResource Get(GsplatAsset asset)
        {
            var key = asset.GetInstanceID();
            if (k_resourceCache.TryGetValue(key, out var cache))
            {
                cache.RefCount++;
                return cache.Resource;
            }

            cache = new Cache
            {
                Resource = asset.CreateResource(),
                RefCount = 1
            };
            k_resourceCache[key] = cache;
            return cache.Resource;
        }

        public static void Release(GsplatAsset asset)
        {
            Release(asset.GetInstanceID());
        }

        public static void Release(int instanceID)
        {
            if (instanceID == 0)
                return;
            if (!k_resourceCache.TryGetValue(instanceID, out var cache))
            {
                Debug.LogWarning("Trying to release a GPU resource that is not cached.");
                return;
            }

            cache.RefCount--;
            if (cache.RefCount != 0) return;
            cache.Resource.Dispose();
            k_resourceCache.Remove(instanceID);
        }
    }
}
