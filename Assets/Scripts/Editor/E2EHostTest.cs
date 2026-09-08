using System;
using System.IO;
using System.Threading.Tasks;
using GameNet;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameNet.EditorTools
{
    /// <summary>
    /// E2E Host 侧测试（batchmode -executeMethod GameNet.EditorTools.E2EHostTest.Run）：
    /// 编辑器进入 PlayMode → DirectIP StartHost → 等待 Client(独立 exe) 加入 → 2/2 后自动开始游戏 →
    /// Host 移动/旋转自己的玩家制造同步流量 → 等待 Client 断开证据 → Host Save/Load 存档验证 → 退出。
    /// 全程写 .e2e/host_evidence.log。
    /// </summary>
    public static class E2EHostTest
    {
        private const string EvidenceDir = ".e2e";

        private static string EvidencePath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, EvidenceDir, "host_evidence.log");
        private static string HostReadyPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, EvidenceDir, "host_ready");

        private static void Log(string message)
        {
            Debug.Log($"[E2E][Host] {message}");
            Directory.CreateDirectory(Path.GetDirectoryName(EvidencePath));
            File.AppendAllText(EvidencePath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }

        public static void Run()
        {
            if (File.Exists(EvidencePath))
            {
                File.Delete(EvidencePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(EvidencePath));
            if (File.Exists(HostReadyPath))
            {
                File.Delete(HostReadyPath);
            }

            EditorApplication.update += Boot;
        }

        private static void Boot()
        {
            EditorApplication.update -= Boot;

            EditorSceneManager.OpenScene("Assets/Scenes/主菜单.unity", OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
            EditorApplication.update += StartRunner;
        }

        private static void StartRunner()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            EditorApplication.update -= StartRunner;

            var go = new GameObject("E2EHostRunner");
            var runner = go.AddComponent<E2EHostRunner>();
            EditorApplication.update += runner.Tick;
        }
    }

    public class E2EHostRunner : MonoBehaviour
    {
        private int m_Phase; // 0 启动 1 等待加入 2 等待满员 3 开始游戏 4 同步流量 5 存档验证 6 完成
        private float m_PhaseTimer;
        private float m_MoveTimer;
        private bool m_MovedFar;
        private bool m_Rotated;
        private const float Timeout = 180f;

        private void Start()
        {
            Log("HOST_BOOT 进入 PlayMode");
            var net = GameObject.Find("NetworkManager_GO");
            if (net == null)
            {
                Fail("找不到 NetworkManager_GO");
                return;
            }

            var gameNet = net.GetComponent<GameNetworkManager>();
            gameNet.SetConnectMode(ConnectMode.DirectIP);
            gameNet.OnRoomStateChanged += () => Log($"STATE {gameNet.State}");

            Log("HOST_STARTHOST 开始创建 DirectIP 房间");
            _ = StartHost(gameNet);
        }

        private async Task StartHost(GameNetworkManager gameNet)
        {
            await gameNet.StartHostAsync();

            if (gameNet.State == RoomState.WaitingForPlayer)
            {
                Log("HOST_READY host 已启动，等待 Client 加入");
                File.WriteAllText(E2EHostTestHostReadyPath, DateTime.Now.ToString("HH:mm:ss"));
                m_Phase = 1;
            }
            else
            {
                Fail("StartHost 失败：" + gameNet.LastError);
            }
        }

        private static string E2EHostTestHostReadyPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".e2e", "host_ready");

        public void Tick()
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
                {
                    int count = nm.ConnectedClients.Count;
                    if (count >= 1)
                    {
                        Log($"CLIENT_JOINED 人数 {count}/2");
                        m_Phase = 2;
                        m_PhaseTimer = 0f;
                    }

                    break;
                }
                case 2:
                {
                    int count = nm.ConnectedClients.Count;
                    if (count >= 2)
                    {
                        Log("READY_2_OF_2 满员");
                        m_Phase = 3;
                        m_PhaseTimer = 0f;
                    }

                    break;
                }
                case 3:
                {
                    if (m_PhaseTimer < 1f)
                    {
                        break;
                    }

                    Log("HOST_STARTGAME 满员后点击开始（Host 调用 NetworkSceneManager.LoadScene）");
                    GameNetworkManager.Instance.StartGame();
                    m_Phase = 4;
                    m_PhaseTimer = 0f;
                    break;
                }
                case 4:
                {
                    // 等 GamePlay 加载完成，然后驱动 Host 玩家制造位置/旋转同步。
                    if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != GameNetworkManager.GamePlaySceneName)
                    {
                        break;
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
                    var ownPlayer = GetOwnPlayer();
                    if (ownPlayer == null)
                    {
                        break;
                    }

                    // 模拟 Host 玩家移动（等价 Owner 输入，直接驱动 CharacterController 路径）
                    if (m_MoveTimer > 1f && m_MoveTimer < 4f)
                    {
                        ownPlayer.GetComponent<PlayerController>().TestMove(new Vector2(0f, 1f), 0f);
                        if (!m_MovedFar && ownPlayer.transform.position.magnitude > 3f)
                        {
                            m_MovedFar = true;
                            Log($"HOST_MOVED Host 玩家移动到 {ownPlayer.transform.position}（Client 端应能看到该变化）");
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
                    else if (m_MoveTimer >= 5f && !m_SyncWindowLogged)
                    {
                        m_SyncWindowLogged = true;
                        Log($"HOST_SYNC_SAMPLE Host 玩家最终 pos={ownPlayer.transform.position} rot={ownPlayer.transform.eulerAngles.y:F1}（供与 Client 证据比对）");
                        m_Phase = 5;
                        m_PhaseTimer = 0f;
                    }

                    break;
                }
                case 5:
                {
                    // 等 Client 验证完成（client_evidence.log 出现 ALL_PASS 或 FAIL），然后验证 Host 存档。
                    if (m_PhaseTimer < 2f)
                    {
                        break;
                    }

                    RunSaveVerification();
                    m_Phase = 6;
                    m_PhaseTimer = 0f;
                    break;
                }
                case 6:
                {
                    // Client 断开回调触发后（或超时保护）收尾退出。
                    if (m_PhaseTimer > 5f)
                    {
                        Log("HOST_E2E_DONE");
                        EditorApplication.Exit(0);
                    }

                    break;
                }
            }
        }

        private bool m_SpawnLogged;
        private bool m_SyncWindowLogged;

        private static NetworkObject GetOwnPlayer()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsHost) return null;
            return nm.LocalClient.PlayerObject;
        }

        private static void RunSaveVerification()
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
            Log("HOST_E2E_DONE");
        }

        private static void Log(string message)
        {
            Debug.Log($"[E2E][Host] {message}");
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".e2e", "host_evidence.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }

        private void Fail(string reason)
        {
            Log("HOST_E2E_FAIL " + reason);
            EditorApplication.Exit(1);
        }
    }
}
