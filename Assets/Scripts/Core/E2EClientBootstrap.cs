using System;
using System.IO;
using System.Threading;
using GameNet;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// E2E 双实例测试用的 Client 引导（也支持编辑器调用）。
/// 只在命令行带 --e2e-client 时生效，正常启动游戏完全不受影响。
/// 流程：等待 host_ready 文件 → 以 DirectIP 加入 → 等待网络场景同步到 GamePlay →
/// 验证两名玩家出生与位置/旋转同步 → 记录证据 → 退出。
/// </summary>
public static class E2EClientBootstrap
{
    private const string EvidenceDir = ".e2e";
    private static string EvidencePath => Path.Combine(Application.dataPath, "..", EvidenceDir, "client_evidence.log");

    public static bool TryRunFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        foreach (string a in args)
        {
            if (a == "--e2e-client")
            {
                Run();
                return true;
            }
        }

        return false;
    }

    internal static void Log(string message)
    {
        Debug.Log($"[E2E][Client] {message}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EvidencePath));
            File.AppendAllText(EvidencePath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[E2E][Client] 写证据失败：{e.Message}");
        }
    }

    private static void Run()
    {
        Log("CLIENT_BOOT");
        var host = new GameObject("E2EClientRunner").AddComponent<E2EClientRunner>();
        UnityEngine.Object.DontDestroyOnLoad(host.gameObject);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootFromCommandLine()
    {
        TryRunFromCommandLine();
    }
}

/// <summary>实际驱动 Client E2E 流程的 MonoBehaviour。</summary>
public class E2EClientRunner : MonoBehaviour
{
    private bool m_Started;
    private float m_WaitTimer;
    private int m_Phase; // 0 等host 1 加入 2 等GamePlay 3 等同步 4 完成
    private Vector3 m_LastRemotePos;
    private float m_RemotePosSampleTimer;
    private bool m_PosSyncVerified;
    private bool m_RotSyncVerified;
    private bool m_SaveProtectionTested;
    private float m_RotSampleTimer;
    private float m_RotBaseline = -1f;
    private float m_PhaseTimer;
    private const float Timeout = 150f;

    private static void Log(string message)
    {
        E2EClientBootstrap.Log(message);
    }

    private void Update()
    {
        m_PhaseTimer += Time.unscaledDeltaTime;

        if (m_PhaseTimer > Timeout)
        {
            Fail("超时");
            return;
        }

        switch (m_Phase)
        {
            case 0: WaitForHost(); break;
            case 1: TryJoin(); break;
            case 2: WaitForGamePlay(); break;
            case 3: VerifySync(); break;
        }
    }

    private static string HostReadyPath => Path.Combine(Application.dataPath, "..", ".e2e", "host_ready");

    private void WaitForHost()
    {
        if (File.Exists(HostReadyPath))
        {
            Log("HOST_READY 发现，开始加入");
            m_Phase = 1;
        }
    }

    private async void TryJoin()
    {
        if (m_Started || GameNetworkManager.Instance == null || GameNetworkManager.Instance.State != RoomState.Idle)
        {
            return;
        }

        m_Started = true;
        GameNetworkManager.Instance.SetConnectMode(ConnectMode.DirectIP);

        // UI 驱动：与真人点击完全同路径 —— 打开加入面板 → 填房间码 → 点"加入"按钮。
        var netGo = GameObject.Find("NetworkManager_GO");
        var ui = netGo != null ? netGo.GetComponent<MainMenuUIController>() : null;
        if (ui != null && ui.Btn_JoinClient != null)
        {
            ui.OnClickJoinGame();
            if (ui.Input_RoomCode != null)
            {
                ui.Input_RoomCode.text = "127.0.0.1";
            }
            ui.Btn_JoinClient.onClick.Invoke();
            Log("CLIENT_UI_CLICKED 已点击 加入游戏/加入 按钮（房间码=127.0.0.1）");
        }
        else
        {
            Log("开始 DirectIP 加入 127.0.0.1:7777");
            await GameNetworkManager.Instance.JoinClientAsync("127.0.0.1");
        }

        if (GameNetworkManager.Instance.State == RoomState.Error)
        {
            Fail("加入失败：" + GameNetworkManager.Instance.LastError);
        }
        else
        {
            m_Phase = 2;
        }
    }

    private void WaitForGamePlay()
    {
        if (GameNetworkManager.Instance == null) return;

        if (GameNetworkManager.Instance.State == RoomState.Error)
        {
            Fail("房间进入 Error：" + GameNetworkManager.Instance.LastError);
            return;
        }

        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsConnectedClient && Time.unscaledTime - m_WaitTimer > 1f)
        {
            m_WaitTimer = Time.unscaledTime;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient &&
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == GameNetworkManager.GamePlaySceneName)
        {
            Log($"GAMEPLAY_SYNCED 网络场景已同步：{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            m_Phase = 3;
        }
    }

    private void VerifySync()
    {
        m_RemotePosSampleTimer += Time.unscaledDeltaTime;

        ulong remoteId = 0;
        NetworkObject own = null;
        NetworkObject remote = null;

        foreach (var netObj in FindObjectsOfType<NetworkObject>())
        {
            if (!netObj.IsPlayerObject) continue;
            if (netObj.IsOwner) own = netObj;
            else { remote = netObj; remoteId = netObj.OwnerClientId; }
        }

        if (own == null || remote == null)
        {
            return;
        }

        if (!m_SaveProtectionTested)
        {
            m_SaveProtectionTested = true;
            bool allowed = SaveSystem.SaveGame("e2e_client_attempt", new GameSaveData { saveName = "client非法写入尝试" });
            Log(allowed ? "SAVE_PROTECTION_FAIL Client 居然写入了存档！" : "SAVE_PROTECTED Client 写存档已被拦截");
            var files = SaveSystem.LoadAllSaveFiles();
            Log(files.Count == 0 ? "SAVE_LIST_PROTECTED Client 读取存档列表已被拦截（返回空）" : "SAVE_LIST_FAIL Client 读取到存档列表");
        }

        if (!m_PosSyncVerified)
        {
            Log($"SPAWN_VERIFIED 本机玩家 pos={own.transform.position} 远端玩家(ClientId={remoteId}) pos={remote.transform.position}");

            // 远端位置随时间变化 → Host 移动同步到了 Client。
            if (m_RemotePosSampleTimer >= 1.5f)
            {
                if (m_LastRemotePos != Vector3.zero && (remote.transform.position - m_LastRemotePos).magnitude > 0.02f)
                {
                    m_PosSyncVerified = true;
                    Log($"POSITION_SYNC_VERIFIED 远端玩家位置从 {m_LastRemotePos} 变化到 {remote.transform.position}");
                }

                m_LastRemotePos = remote.transform.position;
                m_RemotePosSampleTimer = 0f;
            }
        }
        else if (!m_RotSyncVerified)
        {
            m_RotSampleTimer += Time.unscaledDeltaTime;
            float remoteRotY = remote.transform.eulerAngles.y;

            if (m_RotBaseline < 0f)
            {
                // 记录进入旋转验证阶段的基准角（插值是平滑的，需对比阶段起点而非上一帧）。
                m_RotBaseline = remoteRotY;
            }

            if (m_RotSampleTimer > 1f)
            {
                m_RotSampleTimer = 0f;
                Log($"ROT_SAMPLE remote rotY={remoteRotY:F1} baseline={m_RotBaseline:F1} ownRot={own.transform.eulerAngles.y:F1}");
            }

            if (Mathf.Abs(Mathf.DeltaAngle(m_RotBaseline, remoteRotY)) > 15f)
            {
                m_RotSyncVerified = true;
                Log($"ROTATION_SYNC_VERIFIED 远端玩家旋转从基准 {m_RotBaseline:F1}° 变化到 {remoteRotY:F1}°");
                Log("CLIENT_E2E_ALL_PASS");
                Application.Quit(0);
            }
        }
    }

    private float m_LastRemoteRotY;

    private void Fail(string reason)
    {
        Log($"CLIENT_E2E_FAIL {reason}");
        Application.Quit(1);
    }
}
