using System;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace GameNet.EditorTools
{
    /// <summary>
    /// 退出播放模式前主动关闭 Netcode / UTP。
    ///
    /// 背景：Unity Transport 的 NetworkDriver 内部持有一批 Native 容器（NativeArray、NativeQueue 等）。
    /// 在仍然联机的状态下直接退出播放模式，这些 native 分配不会被释放，控制台就会刷：
    /// "A Native Collection has not been disposed, resulting in a memory leak."
    ///
    /// ExitingPlayMode 阶段对象尚未销毁，此时 Shutdown 是安全的，也是唯一能让 UTP 正常 Dispose 的时机。
    /// 打包运行时不经过这里，因此 GameNetworkManager.OnApplicationQuit 里另有一道兜底。
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeNetworkCleanup
    {
        static PlayModeNetworkCleanup()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                return;
            }

            try
            {
                // 注意：不能只在 IsListening 时才关。UTP 的 NetworkDriver 在
                // StartHostAsync/JoinClientAsync 的中途（鉴权、Relay 分配还没返回）
                // 就已经分配好 Native 容器了，那时尚未进入 Listening；
                // 这种“半途退出”正是告警的高发场景，所以这里无条件 Shutdown，
                // 再单独关一次 Transport（Shutdown 内部没走到时兜底）。
                Debug.Log("[Net] 退出播放模式：主动 Shutdown，释放 UTP 的 Native 资源。");
                nm.Shutdown();

                var transport = nm.NetworkConfig != null ? nm.NetworkConfig.NetworkTransport : null;
                if (transport != null)
                {
                    transport.Shutdown();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Net] 退出播放模式 Shutdown 时出错：{e.Message}");
            }
        }
    }
}
