using UnityEngine;

namespace Gsplat
{
    [ExecuteInEditMode]
    public class GsplatRenderer : MonoBehaviour
    {
        public GsplatAsset gsplatAsset;

        void OnEnable()
        {
            if (gsplatAsset == null)
            {
                Debug.LogError("GsplatAsset is not assigned.");
                return;
            }

            // Example: Log the number of splats
            Debug.Log($"Number of splats: {gsplatAsset.numSplats}");

            Debug.Log($"shs[0, 0]={gsplatAsset.shs[0]}");
            Debug.Log($"shs[1, 0]={gsplatAsset.shs[15 * 2]}");
            Debug.Log($"{gsplatAsset.mesh.vertices.Length}");
        }
    }
}