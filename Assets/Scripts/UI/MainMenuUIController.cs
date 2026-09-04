using GameNet;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MainMenu UI 控制器：绑定现有 Canvas 上的 创建游戏 / 加入游戏 按钮，
/// 控制 Panel_CreateRoom / Panel_JoinRoom 显隐，并同步房间码、状态、人数到 UI。
/// </summary>
public class MainMenuUIController : MonoBehaviour
{
    [Header("主菜单按钮")]
    public Button Btn_CreateGame;
    public Button Btn_JoinGame;

    [Header("创建房间面板")]
    public GameObject Panel_CreateRoom;
    public TMP_Text Txt_RoomCode;
    public TMP_Text RoomStatus;
    public TMP_Text PlayerCount;
    public Button Btn_StartHost;
    public Button Btn_StartGame;

    [Header("加入房间面板")]
    public GameObject Panel_JoinRoom;
    public TMP_InputField Input_RoomCode;
    public TMP_Text ErrorText;
    public TMP_Text Txt_JoinStatus;
    public Button Btn_JoinClient;

    private GameNetworkManager Net => GameNetworkManager.Instance;

    private void Awake()
    {
        // 默认：两个面板都隐藏。
        if (Panel_CreateRoom != null) Panel_CreateRoom.SetActive(false);
        if (Panel_JoinRoom != null) Panel_JoinRoom.SetActive(false);

        if (Btn_CreateGame != null) Btn_CreateGame.onClick.AddListener(OnClickCreateGame);
        if (Btn_JoinGame != null) Btn_JoinGame.onClick.AddListener(OnClickJoinGame);
        if (Btn_StartHost != null) Btn_StartHost.onClick.AddListener(OnClickStartHost);
        if (Btn_JoinClient != null) Btn_JoinClient.onClick.AddListener(OnClickJoinClient);
        if (Btn_StartHost != null) Btn_StartHost.interactable = false;
        if (Btn_StartGame != null)
        {
            Btn_StartGame.onClick.AddListener(OnClickStartGame);
            Btn_StartGame.gameObject.SetActive(false);
        }

        if (ErrorText != null) ErrorText.text = string.Empty;
        if (Txt_JoinStatus != null) Txt_JoinStatus.text = string.Empty;
    }

    private void OnEnable()
    {
        if (Net != null)
        {
            Net.OnRoomStateChanged += RefreshRoomUI;
            Net.OnDisconnectMessage += ShowDisconnectMessage;
        }

        RefreshRoomUI();
    }

    private void OnDisable()
    {
        if (Net != null)
        {
            Net.OnRoomStateChanged -= RefreshRoomUI;
            Net.OnDisconnectMessage -= ShowDisconnectMessage;
        }
    }

    // ------------------------------------------------------------------
    // 按钮回调
    // ------------------------------------------------------------------
    public void OnClickCreateGame()
    {
        Debug.Log("[UI] 点击 创建游戏");
        if (Panel_JoinRoom != null) Panel_JoinRoom.SetActive(false);
        if (Panel_CreateRoom != null) Panel_CreateRoom.SetActive(true);
        RefreshRoomUI();
    }

    public void OnClickJoinGame()
    {
        Debug.Log("[UI] 点击 加入游戏");
        if (Panel_CreateRoom != null) Panel_CreateRoom.SetActive(false);
        if (Panel_JoinRoom != null) Panel_JoinRoom.SetActive(true);
        if (ErrorText != null) ErrorText.text = string.Empty;
        RefreshRoomUI();
    }

    public async void OnClickStartHost()
    {
        if (Net == null)
        {
            Debug.LogError("[UI] GameNetworkManager 未初始化。");
            return;
        }

        if (Net.State != RoomState.Idle && Net.State != RoomState.Error)
        {
            return;
        }

        if (Btn_StartHost != null) Btn_StartHost.interactable = false;
        await Net.StartHostAsync();
    }

    public async void OnClickJoinClient()
    {
        if (Net == null)
        {
            Debug.LogError("[UI] GameNetworkManager 未初始化。");
            return;
        }

        if (Net.State != RoomState.Idle && Net.State != RoomState.Error)
        {
            return;
        }

        if (ErrorText != null) ErrorText.text = string.Empty;

        string code = Input_RoomCode != null ? Input_RoomCode.text : string.Empty;
        await Net.JoinClientAsync(code);

        if (Net.State == RoomState.Error && ErrorText != null)
        {
            ErrorText.text = Net.LastError;
        }
    }

    public void OnClickBackToMenu()
    {
        Debug.Log("[UI] 返回主菜单");
        if (Panel_CreateRoom != null) Panel_CreateRoom.SetActive(false);
        if (Panel_JoinRoom != null) Panel_JoinRoom.SetActive(false);
    }

    public void OnClickStartGame()
    {
        Debug.Log("[UI] 点击 开始游戏");
        if (Net != null)
        {
            Net.StartGame();
        }
    }

    public void OnClickQuit()
    {
        Debug.Log("[UI] 点击 退出");
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ------------------------------------------------------------------
    // 房间 UI 刷新
    // ------------------------------------------------------------------
    private void RefreshRoomUI()
    {
        if (Net == null)
        {
            return;
        }

        int count = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
            ? NetworkManager.Singleton.ConnectedClients.Count
            : 0;

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        if (Txt_RoomCode != null)
        {
            Txt_RoomCode.text = string.IsNullOrEmpty(Net.JoinCode) ? "正在创建房间…" : $"房间码：{Net.JoinCode}";
        }

        if (RoomStatus != null)
        {
            RoomStatus.text = Net.State switch
            {
                RoomState.Idle => "待机",
                RoomState.Creating => "正在创建房间…",
                RoomState.WaitingForPlayer => isHost ? "等待玩家加入…" : "正在连接主机…",
                RoomState.PlayerJoined => isHost ? "玩家已加入，等待满员" : "连接成功！等待主机开始",
                RoomState.ReadyToStart => "玩家已满员，可以开始游戏！",
                RoomState.Starting => "正在开始游戏…",
                RoomState.LoadingGame => "正在加载 GamePlay…",
                RoomState.Error => Net.LastError,
                _ => Net.State.ToString()
            };
        }

        if (PlayerCount != null)
        {
            PlayerCount.text = $"{count}/{GameNetworkManager.MaxPlayers} 玩家";
        }

        // 面板互斥保护：两个面板同时可见时关闭加入面板并记录日志。
        if (Panel_CreateRoom != null && Panel_JoinRoom != null &&
            Panel_CreateRoom.activeSelf && Panel_JoinRoom.activeSelf)
        {
            Debug.LogWarning("[UI] 检测到 创建/加入 面板同时激活，已强制关闭加入面板。");
            Panel_JoinRoom.SetActive(false);
        }

        // Host：创建按钮只在未建房时可点；满员后显示"开始游戏"按钮。
        if (Btn_StartHost != null)
        {
            bool idle = Net.State == RoomState.Idle || Net.State == RoomState.Error;
            Btn_StartHost.interactable = isHost && idle;
        }

        if (Btn_StartGame != null)
        {
            bool canStart = isHost && Net.State == RoomState.ReadyToStart;
            if (Btn_StartGame.gameObject.activeSelf != canStart)
            {
                Btn_StartGame.gameObject.SetActive(canStart);
            }
            Btn_StartGame.interactable = canStart;
        }

        // Client 成功提示（绿色）。
        if (Txt_JoinStatus != null)
        {
            bool clientOk = !isHost && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening &&
                            (Net.State == RoomState.PlayerJoined || Net.State == RoomState.ReadyToStart ||
                             Net.State == RoomState.Starting || Net.State == RoomState.LoadingGame);
            Txt_JoinStatus.text = clientOk
                ? (Net.State == RoomState.PlayerJoined ? "连接成功！等待主机开始…" : "主机已开始，正在进入游戏…")
                : string.Empty;
        }

        if (Net.State == RoomState.Error && ErrorText != null && Panel_JoinRoom != null && Panel_JoinRoom.activeSelf)
        {
            ErrorText.text = Net.LastError;
        }
    }

    private void ShowDisconnectMessage(string message)
    {
        if (ErrorText != null)
        {
            ErrorText.text = message;
        }
    }

    private void Update()
    {
        // Client 握手是回调驱动的，兜底每帧刷新一次状态文本。
        if (Net != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            RefreshRoomUI();
        }
    }
}
