using System;
using System.IO;
using GameNet;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// E2E 双实例测试用的 Host 引导：命令行带 --e2e-host 时生效。
/// 流程：DirectIP StartHost → 写 host_ready → 等 Client 加入 → 满员自动开始游戏 →
/// 驱动 Host 玩家制造位置/旋转同步 → Client 断开后做 Host 存档验证 → 退出。
/// </summary>
public static class E2EHostBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootFromCommandLine()
    {
        foreach (string a in Environment.GetCommandLineArgs())
        {
            if (a == "--e2e-host")
            {
                var log = new GameObject("E2EHostRunner").AddComponent<E2EHostPlayerRunner>();
                UnityEngine.Object.DontDestroyOnLoad(log.gameObject);
                return;
            }
        }
    }
}

/// <summary>实际驱动 Host E2E 流程的 MonoBehaviour。</summary>
public class E2EHostPlayerRunner : MonoBehaviour
{
    private int m_Phase; // 0 启动 1 等加入 2 等满员 3 开始游戏 4 同步流量 5 存档 6 完成
    private float m_PhaseTimer;
    private float m_MoveTimer;
    private bool m_SpawnLogged;
    private bool m_MovedFar;
    private bool m_Rotated;
    private bool m_SyncSampled;
    private const float Timeout = 300f;
    private float m_WaitLogTimer;

    private static string EvidencePath => Path.Combine(Application.dataPath, "..", ".e2e", "host_evidence.log");
    private static string HostReadyPath => Path.Combine(Application.dataPath, "..", ".e2e", "host_ready");

    private static void Log(string message)
    {
        Debug.Log($"[E2E][Host] {message}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EvidencePath));
            File.AppendAllText(EvidencePath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[E2E][Host] 写证据失败：{e.Message}");
        }
    }

    private void Start()
    {
        Log("HOST_BOOT 进入运行时");
        var net = GameObject.Find("NetworkManager_GO");
        if (net == null)
        {
            Fail("找不到 NetworkManager_GO");
            return;
        }

        var gameNet = net.GetComponent<GameNetworkManager>();
        gameNet.SetConnectMode(ConnectMode.DirectIP);
        gameNet.OnRoomStateChanged += () => Log($"STATE {gameNet.State}");
        _ = StartHost(gameNet);
    }

    private async System.Threading.Tasks.Task StartHost(GameNetworkManager gameNet)
    {
        await gameNet.StartHostAsync();

        if (gameNet.State == RoomState.WaitingForPlayer)
        {
            Log("HOST_READY host 已启动，等待 Client 加入");
            Directory.CreateDirectory(Path.GetDirectoryName(HostReadyPath));
            File.WriteAllText(HostReadyPath, DateTime.Now.ToString("HH:mm:ss"));
            m_Phase = 1;
        }
        else
        {
            Fail("StartHost 失败：" + gameNet.LastError);
        }
    }

    private void Update()
    {
        m_PhaseTimer += Time.unscaledDeltaTime;

        if (m_PhaseTimer > Timeout)
        {
            Fail("超时 phase=" + m_Phase);
            return;
        }

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            return;
        }

        switch (m_Phase)
        {
            case 1:
                if (nm.ConnectedClients.Count >= 1)
                {
                    Log($"CLIENT_JOINED 人数 {nm.ConnectedClients.Count}/2");
                    m_Phase = 2;
                    m_PhaseTimer = 0f;
                }
                break;
            case 2:
                if (nm.ConnectedClients.Count >= 2)
                {
                    Log("READY_2_OF_2 满员");
                    m_Phase = 3;
                    m_PhaseTimer = 0f;
                }
                break;
            case 3:
                if (m_PhaseTimer < 1f) break;
                Log("HOST_STARTGAME 满员后开始游戏（Host 调用 NetworkSceneManager.LoadScene）");
                GameNetworkManager.Instance.StartGame();
                m_Phase = 4;
                m_PhaseTimer = 0f;
                break;
            case 4:
                DriveSyncTraffic();
                break;
            case 5:
                if (m_PhaseTimer > 2f)
                {
                    RunSaveVerification();
                    m_Phase = 6;
                    m_PhaseTimer = 0f;
                }
                break;
            case 6:
            {
                // 等 Client 端完成位置/旋转同步验证（或明确失败）后再退出。
                m_WaitLogTimer += Time.unscaledDeltaTime;
                if (m_WaitLogTimer > 1f)
                {
                    m_WaitLogTimer = 0f;
                    try
                    {
                        string clientLog = File.ReadAllText(Path.Combine(Application.dataPath, "..", ".e2e", "client_evidence.log"));
                        if (clientLog.Contains("CLIENT_E2E_ALL_PASS") || clientLog.Contains("CLIENT_E2E_FAIL"))
                        {
                            Log("HOST_E2E_DONE");
                            Application.Quit(0);
                        }
                    }
                    catch (IOException)
                    {
                        // client 日志尚未生成，继续等待。
                    }
                }

                break;
            }
        }
    }

    private void DriveSyncTraffic()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != GameNetworkManager.GamePlaySceneName)
        {
            return;
        }

        if (m_PhaseTimer > 0.5f && !m_SpawnLogged)
        {
            m_SpawnLogged = true;
            int players = 0;
            foreach (var netObj in FindObjectsOfType<NetworkObject>())
            {
                if (netObj.IsPlayerObject)
                {
                    players++;
                    Log($"HOST_SPAWN 玩家对象 ClientId={netObj.OwnerClientId} pos={netObj.transform.position}");
                }
            }
            Log($"HOST_SPAWN_COUNT 玩家数量={players}（期望 2）");
        }

        m_MoveTimer += Time.unscaledDeltaTime;
        NetworkManager nm = NetworkManager.Singleton;
        var ownPlayer = nm != null && nm.IsHost ? nm.LocalClient.PlayerObject : null;
        if (ownPlayer == null)
        {
            return;
        }

        if (m_MoveTimer > 1f && m_MoveTimer < 4f)
        {
            ownPlayer.GetComponent<PlayerController>().TestMove(new Vector2(0f, 1f), 0f);
            if (!m_MovedFar && ownPlayer.transform.position.magnitude > 3f)
            {
                m_MovedFar = true;
                Log($"HOST_MOVED Host 玩家移动到 {ownPlayer.transform.position}");
            }
        }
        else if (m_MoveTimer >= 4f && m_MoveTimer < 5f)
        {
            ownPlayer.GetComponent<PlayerController>().TestMove(Vector2.zero, 1f);
            if (!m_Rotated && Mathf.Abs(Mathf.DeltaAngle(0f, ownPlayer.transform.eulerAngles.y)) > 30f)
            {
                m_Rotated = true;
                Log($"HOST_ROTATED Host 玩家旋转到 {ownPlayer.transform.eulerAngles.y:F1}°");
            }
        }
        else if (m_MoveTimer >= 5f && !m_SyncSampled)
        {
            m_SyncSampled = true;
            Log($"HOST_SYNC_SAMPLE Host 玩家最终 pos={ownPlayer.transform.position} rot={ownPlayer.transform.eulerAngles.y:F1}");
            m_Phase = 5;
            m_PhaseTimer = 0f;
        }
    }

    private void RunSaveVerification()
    {
        Log("SAVE_TEST 开始 Host 存档验证");

        var data = new GameSaveData
        {
            saveName = "E2E自动存档",
            sceneName = GameNetworkManager.GamePlaySceneName,
            chapterIndex = 1
        };
        data.trickItems.Add(new TrickItemRecord { itemId = "banana_peel", isUsed = false, isPlaced = false });
        data.trickItems.Add(new TrickItemRecord { itemId = "marble_bag", isUsed = true, isPlaced = true });
        data.neighborStates.Add(new NeighborStateRecord { stateId = "suspicion", value = 42f });
        data.players.Add(new PlayerRecord { characterId = "jitMin", posX = 1f, posY = 1.2f, posZ = 4f, rotY = 0f });

        bool saved = SaveSystem.SaveGame("e2e_test", data);
        Log(saved ? "HOST_SAVE_OK Host 保存成功" : "HOST_SAVE_FAIL 保存失败");

        var loaded = SaveSystem.LoadOneSave("e2e_test");
        Log(loaded != null && loaded.trickItems.Count == 2
            ? $"HOST_LOAD_OK Host 读取成功：saveName={loaded.saveName}, items={loaded.trickItems.Count}, neighborStates={loaded.neighborStates.Count}"
            : "HOST_LOAD_FAIL 读取失败");

        var all = SaveSystem.LoadAllSaveFiles();
        Log(all.Contains("e2e_test") ? "HOST_LIST_OK 存档列表包含 e2e_test" : "HOST_LIST_FAIL 列表缺失");

        SaveSystem.DeleteSave("e2e_test");

        // 断开处理：Host 在 Client 退出后回到等待状态（由 OnClientDisconnectCallback 记录）。
    }

    private void Fail(string reason)
    {
        Log("HOST_E2E_FAIL " + reason);
        Application.Quit(1);
    }
}
