using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public class GsplatRenderSystem
    {
        public static GsplatRenderSystem Instance => s_instance ??= new GsplatRenderSystem();
        static GsplatRenderSystem s_instance;
        CommandBuffer m_commandBuffer;
        readonly HashSet<GsplatRenderer> m_gsplats = new();
        readonly HashSet<Camera> m_cameraCommandBuffersDone = new();
        readonly List<GsplatRenderer> m_activeGsplats = new();
        GpuSorting m_sorter;

        public bool Valid => m_sorter is { Valid: true };

        public void InitSorter(ComputeShader computeShader)
        {
            m_sorter = computeShader ? new GpuSorting(computeShader) : null;
        }

        public void RegisterGsplat(GsplatRenderer gsplat)
        {
            if (m_gsplats.Count == 0)
            {
                if (!GraphicsSettings.currentRenderPipeline)
                    Camera.onPreCull += OnPreCullCamera;
            }

            m_gsplats.Add(gsplat);
        }

        public void UnregisterGsplat(GsplatRenderer gsplat)
        {
            if (!m_gsplats.Contains(gsplat))
                return;
            m_gsplats.Remove(gsplat);
            if (m_gsplats.Count == 0)
            {
                if (m_cameraCommandBuffersDone != null)
                {
                    if (m_commandBuffer != null)
                    {
                        foreach (var cam in m_cameraCommandBuffersDone)
                        {
                            if (cam)
                                cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, m_commandBuffer);
                        }
                    }

                    m_cameraCommandBuffersDone.Clear();
                }

                m_activeGsplats.Clear();
                m_commandBuffer?.Dispose();
                m_commandBuffer = null;
                Camera.onPreCull -= OnPreCullCamera;
            }
        }

        public bool GatherSplatsForCamera(Camera cam)
        {
            if (cam.cameraType == CameraType.Preview)
                return false;
            // gather all active & valid splat objects
            m_activeGsplats.Clear();
            foreach (var gs in m_gsplats)
            {
                if (gs == null || !gs.isActiveAndEnabled || !gs.Valid)
                    continue;
                m_activeGsplats.Add(gs);
            }

            if (m_activeGsplats.Count == 0)
                return false;

            return true;
        }

        void InitialClearCmdBuffer(Camera cam)
        {
            m_commandBuffer ??= new CommandBuffer { name = "SortGsplats" };
            if (!GraphicsSettings.currentRenderPipeline && cam &&
                !m_cameraCommandBuffersDone.Contains(cam))
            {
                cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, m_commandBuffer);
                m_cameraCommandBuffersDone.Add(cam);
            }

            m_commandBuffer.Clear();
        }

        void OnPreCullCamera(Camera camera)
        {
            if (!Valid || !GsplatSettings.Instance.Valid || !GatherSplatsForCamera(camera))
                return;

            InitialClearCmdBuffer(camera);

            foreach (var gs in m_activeGsplats)
            {
                var sorterArgs = gs.SorterArgs;
                sorterArgs.MatrixMv = camera.worldToCameraMatrix * gs.transform.localToWorldMatrix;
                m_sorter.Dispatch(m_commandBuffer, sorterArgs);
            }
        }
    }
}