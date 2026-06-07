// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;


namespace Gsplat
{
    [InitializeOnLoad]
    public static class GsplatPlayerLoopHook
    {
        static bool s_installed;

        static GsplatPlayerLoopHook()
        {
            Install();
            EditorApplication.playModeStateChanged += _ => { Install(); };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RuntimeInit()
        {
            Install();
        }

        static void Install()
        {
            if (s_installed)
                return;

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            InsertBefore<PostLateUpdate>(ref loop, typeof(GsplatPlayerLoopHook), Update);
            PlayerLoop.SetPlayerLoop(loop);
            s_installed = true;
        }

        static bool InsertBefore<T>(ref PlayerLoopSystem root, System.Type type, PlayerLoopSystem.UpdateFunction fn)
        {
            if (root.subSystemList == null)
                return false;

            for (var i = 0; i < root.subSystemList.Length; i++)
            {
                ref var sys = ref root.subSystemList[i];
                if (sys.type == typeof(T))
                {
                    var list = sys.subSystemList?.ToList() ?? new List<PlayerLoopSystem>();
                    list.Insert(0, new PlayerLoopSystem { type = type, updateDelegate = fn });
                    sys.subSystemList = list.ToArray();
                    return true;
                }

                if (InsertBefore<T>(ref sys, type, fn))
                    return true;
            }

            return false;
        }
        
        static void Update()
        {
            GsplatSorter.Instance.Update();
        }
    }
}