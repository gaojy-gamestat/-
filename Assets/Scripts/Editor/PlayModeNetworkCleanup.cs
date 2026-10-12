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
            if (nm == null || !nm.IsListening)
            {
                return;
            }

            try
            {
                Debug.Log("[Net] 退出播放模式：主动 Shutdown，释放 UTP 的 Native 资源。");
                nm.Shutdown();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Net] 退出播放模式 Shutdown 时出错：{e.Message}");
            }
        }
    }
}
