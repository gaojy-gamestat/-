using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
using UnityEngine;

namespace GameNet
{
    /// <summary>
    /// 房间状态机：Idle → Creating → WaitingForPlayer → PlayerJoined → ReadyToStart → Starting → LoadingGame（Error 可从任意状态进入）。
    /// Host 负责创建 Relay Allocation、显示 Join Code、开房；Client 只负责用 Join Code 加入。
    /// 场景切换只允许 Host 通过 NGO NetworkSceneManager 执行，Client 永远跟随网络场景同步。
    /// </summary>
    public enum RoomState
    {
        Idle,
        Creating,
        WaitingForPlayer,
        PlayerJoined,
        ReadyToStart,
        Starting,
        LoadingGame,
        Error
    }

    /// <summary>
    /// 联机方式：Relay（默认，走 UGS）或 DirectIP（本机/局域网调试用，无需 UGS）。
    /// </summary>
    public enum ConnectMode
    {
        Relay,
        DirectIP
    }

    public class GameNetworkManager : MonoBehaviour
    {
        public const string MainMenuSceneName = "MainMenu";
        public const string GamePlaySceneName = "GamePlay";
        public const int MaxPlayers = 2;

        private static GameNetworkManager s_Instance;

        public static GameNetworkManager Instance => s_Instance;

        [Header("联机方式（正式走 Relay，本机联调可用 DirectIP）")]
        [SerializeField]
        private ConnectMode connectMode = ConnectMode.Relay;

        [Header("DirectIP 调试参数")]
        [SerializeField]
        private string directAddress = "127.0.0.1";

        [SerializeField]
        private ushort directPort = 7777;

        [Header("Relay 区域（留空则自动选择）")]
        [SerializeField]
        private string relayRegion = "";

        private RoomState m_State = RoomState.Idle;
        private string m_LastError = string.Empty;
        private string m_JoinCode = string.Empty;
        private bool m_InitializingServices;

        /// <summary>当前房间状态。</summary>
        public RoomState State => m_State;

        /// <summary>最近一次错误信息（给 UI/日志用）。</summary>
        public string LastError => m_LastError;

        /// <summary>Host 创建房间成功后的 Relay Join Code。</summary>
        public string JoinCode => m_JoinCode;

        /// <summary>状态或人数变化时通知 UI。</summary>
        public event Action OnRoomStateChanged;

        /// <summary>Client 收到断开/被断开时通知 UI。</summary>
        public event Action<string> OnDisconnectMessage;

        public NetworkManager NM => NetworkManager.Singleton;

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (NetworkManager.Singleton == null)
            {
                SetError("NetworkManager.Singleton 为空，请确认场景中 NetworkManager 组件已配置。");
                return;
            }

            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure += HandleTransportFailure;
            NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;

            SetState(RoomState.Idle);
        }

        // Host-authoritative：GamePlay 网络场景加载完成后，为所有还没有玩家对象的连接统一生成玩家。
        private void HandleLoadComplete(ulong clientId, string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            {
                return;
            }

            if (sceneName != GamePlaySceneName || loadSceneMode != UnityEngine.SceneManagement.LoadSceneMode.Single)
            {
                return;
            }

            SpawnMissingPlayers();
        }

        private void SpawnMissingPlayers()
        {
            var playerPrefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
            if (playerPrefab == null)
            {
                Debug.LogError("[Net][Host] PlayerPrefab 未配置，无法生成玩家。");
                return;
            }

            foreach (var pair in NetworkManager.Singleton.ConnectedClients)
            {
                if (pair.Value.PlayerObject != null)
                {
                    continue;
                }

                bool isHostClient = pair.Key == NetworkManager.Singleton.LocalClientId;
                var spawnPos = isHostClient ? new Vector3(0f, 1.2f, 4f) : new Vector3(0f, 1.2f, -4f);
                var spawnRot = isHostClient ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);

                var playerObject = UnityEngine.Object.Instantiate(playerPrefab, spawnPos, spawnRot);
                var networkObject = playerObject.GetComponent<NetworkObject>();
                networkObject.SpawnAsPlayerObject(pair.Key, true);
                playerObject.GetComponent<PlayerController>()?.ResetVerticalVelocity();
                Debug.Log($"[Net][Host] 已为 ClientId={pair.Key} 生成玩家，出生点={spawnPos}");
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
                NetworkManager.Singleton.OnTransportFailure -= HandleTransportFailure;
                if (m_SceneEventsHooked)
                {
                    NetworkManager.Singleton.SceneManager.OnLoadComplete -= HandleLoadComplete;
                }
            }

            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        private bool m_SceneEventsHooked;

        /// <summary>网络启动后再订阅场景事件（NGO 启动时会创建真正的 NetworkSceneManager 实例）。</summary>
        private void HookSceneEvents()
        {
            if (m_SceneEventsHooked || NetworkManager.Singleton == null)
            {
                return;
            }

            NetworkManager.Singleton.SceneManager.OnLoadComplete += HandleLoadComplete;
            m_SceneEventsHooked = true;
        }

        // ------------------------------------------------------------------
        // Host 链路：创建房间（Authentication → Relay Allocation → Join Code → StartHost）
        // ------------------------------------------------------------------
        public async Task StartHostAsync()
        {
            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("[Net] 已经在联机中，忽略重复创建房间。");
                return;
            }

            m_LastError = string.Empty;
            SetState(RoomState.Creating);

            try
            {
                await EnsureServicesReadyAsync();

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

                if (connectMode == ConnectMode.Relay)
                {
                    Debug.Log("[Net][Host] 开始创建 Relay Allocation…");
                    // 双人房：Host 自己 1 个连接 + 预留 1 个给 Client。
                    Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1, string.IsNullOrEmpty(relayRegion) ? null : relayRegion);
                    m_JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                    Debug.Log($"[Net][Host] Relay Allocation 创建成功，Join Code = {m_JoinCode}");

                    // 当前项目锁定的 com.unity.services.relay 1.2.0（官方最新版）不包含 AllocationUtils，
                    // 当前版本的正确写法是 Unity.Transport.RelayServerData(Allocation/JoinAllocation, connectionType) 构造器；
                    // （旧教程的过时写法是手动填 host/port/allocationIdBytes 的 RelayServerData 构造，此处并非该写法。）
                    var relayServerData = new RelayServerData(allocation, "dtls");
                    transport.SetRelayServerData(relayServerData);
                }
                else
                {
                    Debug.Log($"[Net][Host] DirectIP 模式启动：{directAddress}:{directPort}");
                    transport.SetConnectionData(directAddress, directPort, "0.0.0.0");
                    m_JoinCode = directAddress;
                }

                if (NetworkManager.Singleton.StartHost())
                {
                    HookSceneEvents();
                    Debug.Log("[Net][Host] StartHost 成功，等待 Client 加入…");
                    SetState(RoomState.WaitingForPlayer);
                }
                else
                {
                    SetError("StartHost 失败，请查看日志。");
                }
            }
            catch (AuthenticationException e)
            {
                SetError($"Authentication 失败：{e.Message}");
            }
            catch (RelayServiceException e)
            {
                SetError($"Relay 创建房间失败（Reason={e.Reason}）：{e.Message}");
            }
            catch (ServicesInitializationException e)
            {
                SetError($"Unity Services 初始化失败：{e.Message}");
            }
            catch (Exception e)
            {
                SetError($"创建房间出现未知异常：{e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Client 链路：加入房间（Authentication → Join Allocation → StartClient）
        // ------------------------------------------------------------------
        public async Task JoinClientAsync(string rawJoinCode)
        {
            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("[Net] 已经在联机中，忽略重复加入。");
                return;
            }

            string joinCode = (rawJoinCode ?? string.Empty).Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(joinCode))
            {
                SetError("请输入房间码（Join Code）。");
                return;
            }

            m_LastError = string.Empty;
            SetState(RoomState.Creating);

            try
            {
                await EnsureServicesReadyAsync();

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

                if (connectMode == ConnectMode.Relay)
                {
                    Debug.Log($"[Net][Client] 尝试使用 Join Code 加入：{joinCode}");
                    JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                    var relayServerData = new RelayServerData(joinAllocation, "dtls");
                    transport.SetRelayServerData(relayServerData);
                    m_JoinCode = joinCode;
                }
                else
                {
                    Debug.Log($"[Net][Client] DirectIP 模式连接：{joinCode}:{directPort}");
                    transport.SetConnectionData(joinCode, directPort);
                    m_JoinCode = joinCode;
                }

                if (NetworkManager.Singleton.StartClient())
                {
                    HookSceneEvents();
                    Debug.Log("[Net][Client] StartClient 成功，等待网络场景同步…");
                    // StartClient 是异步握手，连接结果通过 OnClientConnected/OnTransportFailure 回调确认。
                    SetState(RoomState.WaitingForPlayer);
                }
                else
                {
                    SetError("StartClient 失败，请查看日志。");
                }
            }
            catch (AuthenticationException e)
            {
                SetError($"Authentication 失败：{e.Message}");
            }
            catch (RelayServiceException e)
            {
                // 常见：JoinCodeNotFound（房间码写错/房间已过期）。
                SetError(e.Reason == RelayExceptionReason.JoinCodeNotFound
                    ? "房间码无效或房间已关闭，请核对后重试。"
                    : $"加入 Relay 失败（Reason={e.Reason}）：{e.Message}");
            }
            catch (ServicesInitializationException e)
            {
                SetError($"Unity Services 初始化失败：{e.Message}");
            }
            catch (Exception e)
            {
                SetError($"加入房间出现未知异常：{e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Host 开始游戏：唯一允许的 NetworkSceneManager.LoadScene 调用点
        // ------------------------------------------------------------------
        public void StartGame()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            {
                SetError("只有 Host 可以开始游戏。");
                return;
            }

            if (NetworkManager.Singleton.ConnectedClients.Count < MaxPlayers)
            {
                SetError($"玩家未满员（{NetworkManager.Singleton.ConnectedClients.Count}/{MaxPlayers}），不能开始。");
                return;
            }

            SetState(RoomState.Starting);
            Debug.Log("[Net][Host] 通过 NetworkSceneManager 加载 GamePlay…");
            NetworkManager.Singleton.SceneManager.LoadScene(GamePlaySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
            SetState(RoomState.LoadingGame);
        }

        // ------------------------------------------------------------------
        // 连接回调 / 房间人数
        // ------------------------------------------------------------------
        private void HandleClientConnected(ulong clientId)
        {
            int count = NetworkManager.Singleton.ConnectedClients.Count;

            if (NetworkManager.Singleton.IsHost)
            {
                Debug.Log($"[Net][Host] Client {clientId} 已连接，当前 {count}/{MaxPlayers}");
                if (HostInGamePlay)
                {
                    SpawnMissingPlayers();
                }
                SetState(count >= MaxPlayers ? RoomState.ReadyToStart : RoomState.PlayerJoined);
            }
            else
            {
                Debug.Log($"[Net][Client] 已加入房间（本机 ClientId={clientId}），房间 {count}/{MaxPlayers}");
                SetState(RoomState.PlayerJoined);
            }

            NotifyStateChanged();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null)
            {
                return;
            }

            if (NetworkManager.Singleton.IsHost)
            {
                int count = NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ConnectedClients.Count : 0;
                Debug.Log($"[Net][Host] Client {clientId} 断开，剩余 {count}/{MaxPlayers}");
                SetState(count >= MaxPlayers ? RoomState.ReadyToStart : (count > 0 ? RoomState.PlayerJoined : RoomState.WaitingForPlayer));
            }
            else
            {
                Debug.Log("[Net][Client] 与 Host 断开连接。");
                SetState(RoomState.Error);
                m_LastError = "与 Host 的连接已断开（Host 退出或网络中断）。";
                OnDisconnectMessage?.Invoke(m_LastError);
            }

            NotifyStateChanged();
        }

        private void HandleTransportFailure()
        {
            SetError("网络传输层故障（Transport Failure），连接失败。");
            OnDisconnectMessage?.Invoke(m_LastError);
        }

        // Host-authoritative 出生策略：
        // - 只有当 Host 已在 GamePlay 场景时才允许连接即生成玩家（出生点由审批指定）；
        // - 还在 MainMenu 时先不生成玩家，等 GamePlay 网络场景加载完成后由 Host 统一 SpawnAsPlayerObject。
        private bool HostInGamePlay => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == GamePlaySceneName;

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = true;
            response.CreatePlayerObject = NetworkManager.Singleton.IsHost && HostInGamePlay;

            if (response.CreatePlayerObject)
            {
                if (request.ClientNetworkId == NetworkManager.Singleton.LocalClientId)
                {
                    response.Position = new Vector3(0f, 1.2f, 4f);
                    response.Rotation = Quaternion.identity;
                }
                else
                {
                    response.Position = new Vector3(0f, 1.2f, -4f);
                    response.Rotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }

            response.Pending = false;
        }

        // ------------------------------------------------------------------
        // UGS 初始化 + 匿名登录
        // ------------------------------------------------------------------
        private async Task EnsureServicesReadyAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                if (m_InitializingServices)
                {
                    while (UnityServices.State != ServicesInitializationState.Initialized)
                    {
                        await Task.Delay(100);
                    }
                }
                else
                {
                    m_InitializingServices = true;
                    try
                    {
                        Debug.Log("[Net] 初始化 Unity Services…");
                        await UnityServices.InitializeAsync();
                    }
                    finally
                    {
                        m_InitializingServices = false;
                    }
                }
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log("[Net] 匿名登录 Authentication…");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[Net] Authentication 登录成功，PlayerId={AuthenticationService.Instance.PlayerId}");
            }
        }

        // ------------------------------------------------------------------
        // 状态机内部
        // ------------------------------------------------------------------
        private void SetState(RoomState newState)
        {
            if (m_State == newState)
            {
                return;
            }

            m_State = newState;
            Debug.Log($"[Net] 房间状态：{m_State}");
            NotifyStateChanged();
        }

        private void SetError(string message)
        {
            m_LastError = message;
            m_State = RoomState.Error;
            Debug.LogError($"[Net] {message}");
            NotifyStateChanged();
        }

        private void NotifyStateChanged()
        {
            OnRoomStateChanged?.Invoke();
        }

        private void Update()
        {
            // Client 握手期间 StartClient 成功但一直连不上时给出超时反馈。
            if (m_State == RoomState.WaitingForPlayer && NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsClient && NetworkManager.Singleton.IsConnectedClient)
            {
                SetState(RoomState.PlayerJoined);
            }
        }

        // ------------------------------------------------------------------
        // 编辑器调试入口（菜单 / E2E 测试使用）
        // ------------------------------------------------------------------
        public void SetConnectMode(ConnectMode mode)
        {
            connectMode = mode;
        }

        public ConnectMode GetConnectMode() => connectMode;
    }
}
