using System.Collections;
using GameNet;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行时生成的轻量主菜单。
/// 场景文件只保存网络对象，不保存复杂 UGUI 层级，避免 Unity 编辑器反序列化旧 UI 时崩溃。
/// </summary>
public sealed class SafeMainMenuRuntime : MonoBehaviour
{
    [SerializeField]
    private bool startHidden;

    private Canvas m_Canvas;
    private Text m_StatusText;
    private Text m_RoomCodeText;
    private Text m_PlayerCountText;
    private InputField m_RoomCodeInput;
    private Button m_HostButton;
    private Button m_JoinButton;
    private Button m_StartButton;
    private bool m_Bound;
    private bool m_RequestInFlight;
    private float m_NextRefreshTime;

    private GameNetworkManager Net => GameNetworkManager.Instance;

    private void Start()
    {
        Camera uiCamera = EnsureMainMenuCamera();
        BuildUi(uiCamera);
        if (startHidden && m_Canvas != null)
        {
            m_Canvas.enabled = false;
        }
        TryBindNetworkEvents();
        RefreshUi();
    }

    private void Update()
    {
        TryBindNetworkEvents();
        if (Time.unscaledTime >= m_NextRefreshTime)
        {
            m_NextRefreshTime = Time.unscaledTime + 0.25f;
            RefreshUi();
        }
    }

    private void OnDestroy()
    {
        if (m_Bound && Net != null)
        {
            Net.OnRoomStateChanged -= RefreshUi;
            Net.OnDisconnectMessage -= ShowDisconnectMessage;
        }
    }

    private void TryBindNetworkEvents()
    {
        if (m_Bound || Net == null)
        {
            return;
        }

        Net.OnRoomStateChanged += RefreshUi;
        Net.OnDisconnectMessage += ShowDisconnectMessage;
        m_Bound = true;
    }

    private void BuildUi(Camera uiCamera)
    {
        var canvasObject = new GameObject("RuntimeMainMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        canvasObject.layer = 5;
        m_Canvas = canvasObject.GetComponent<Canvas>();
        // 使用 Screen Space - Camera：UI 只显示在主相机的 16:9 取景区域内，
        // 不会铺到 Letterbox 黑边外面，和游玩界面在同一套画幅体系下。
        m_Canvas.renderMode = RenderMode.ScreenSpaceCamera;
        m_Canvas.worldCamera = uiCamera;
        m_Canvas.planeDistance = 100f;
        m_Canvas.sortingOrder = 100;

        // 固定参考分辨率，保证 UI 在任意屏幕/窗口比例下的视觉占比一致。
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvasObject.AddComponent<CanvasScaler>();
        }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.matchWidthOrHeight = 0.5f;

        var panel = CreateImage("Panel", m_Canvas.transform, new Vector2(760f, 520f), new Vector2(0.5f, 0.5f), new Color(0.035f, 0.045f, 0.065f, 0.98f));
        CreateText("Title", panel.transform, "联机大厅", 42, Color.white, new Vector2(0f, 185f), new Vector2(620f, 70f), TextAnchor.MiddleCenter);
        m_StatusText = CreateText("Status", panel.transform, "正在初始化网络…", 22, new Color(0.75f, 0.85f, 1f), new Vector2(0f, 125f), new Vector2(620f, 50f), TextAnchor.MiddleCenter);
        m_RoomCodeText = CreateText("RoomCode", panel.transform, "房间码：-", 26, new Color(1f, 0.86f, 0.35f), new Vector2(0f, 82f), new Vector2(620f, 50f), TextAnchor.MiddleCenter);
        m_PlayerCountText = CreateText("PlayerCount", panel.transform, "0/2 玩家", 20, Color.white, new Vector2(0f, 46f), new Vector2(620f, 36f), TextAnchor.MiddleCenter);

        m_HostButton = CreateButton("CreateRoom", panel.transform, "创建房间", new Vector2(-190f, -30f));
        m_HostButton.onClick.AddListener(OnClickHost);

        m_JoinButton = CreateButton("JoinRoom", panel.transform, "加入房间", new Vector2(190f, -30f));
        m_JoinButton.onClick.AddListener(OnClickJoin);

        m_RoomCodeInput = CreateInput("JoinCodeInput", panel.transform, new Vector2(0f, -110f));
        m_RoomCodeInput.placeholder.GetComponent<Text>().text = "输入房间码（Relay）或主机 IP";

        m_StartButton = CreateButton("StartGame", panel.transform, "开始游戏", new Vector2(0f, -185f));
        m_StartButton.onClick.AddListener(OnClickStartGame);
        m_StartButton.gameObject.SetActive(false);

        CreateText("Hint", panel.transform, "当前为 DirectIP 直连：本机用 127.0.0.1，局域网填写主机 IP", 16, new Color(0.6f, 0.65f, 0.72f), new Vector2(0f, -235f), new Vector2(650f, 32f), TextAnchor.MiddleCenter);
    }

    /// <summary>
    /// 获取或创建主菜单使用的相机（作为 UI 的 Screen Space - Camera 渲染相机），
    /// 并保证该相机挂载 FixedAspectLetterbox，使菜单与游玩画面统一为 16:9 黑边体系。
    /// </summary>
    private static Camera EnsureMainMenuCamera()
    {
        var camera = Camera.main;
        if (camera == null)
        {
            var cameraObject = new GameObject("RuntimeMainMenuCamera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.depth = -100f;
        }

        if (camera.GetComponent<FixedAspectLetterbox>() == null)
        {
            camera.gameObject.AddComponent<FixedAspectLetterbox>();
        }

        return camera;
    }

    private void OnClickHost()
    {
        if (!m_RequestInFlight)
        {
            StartCoroutine(StartHostRoutine());
        }
    }

    private void OnClickJoin()
    {
        if (!m_RequestInFlight)
        {
            StartCoroutine(JoinRoutine());
        }
    }

    // SampleScene 原有的两个主菜单按钮使用这两个公开入口，
    // 这样旧界面也能复用同一套安全联机 UI 和网络流程。
    public void BeginHostFromExternalButton()
    {
        ShowRuntimeUi();
        OnClickHost();
    }

    public void OpenJoinFromExternalButton()
    {
        ShowRuntimeUi();
        if (m_StatusText != null)
        {
            m_StatusText.text = "请输入主机 IP 或 Relay 房间码";
        }

        if (m_RoomCodeInput != null)
        {
            m_RoomCodeInput.Select();
            m_RoomCodeInput.ActivateInputField();
        }
    }

    private void ShowRuntimeUi()
    {
        if (m_Canvas != null)
        {
            m_Canvas.enabled = true;
        }
    }

    private IEnumerator StartHostRoutine()
    {
        m_RequestInFlight = true;
        RefreshUi();
        var task = Net != null ? Net.StartHostAsync() : null;
        if (task != null)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
        }
        m_RequestInFlight = false;
        RefreshUi();
    }

    private IEnumerator JoinRoutine()
    {
        m_RequestInFlight = true;
        RefreshUi();
        var task = Net != null ? Net.JoinClientAsync(m_RoomCodeInput != null ? m_RoomCodeInput.text : string.Empty) : null;
        if (task != null)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
        }
        m_RequestInFlight = false;
        RefreshUi();
    }

    private void OnClickStartGame()
    {
        if (Net != null)
        {
            Net.StartGame();
        }
    }

    private void RefreshUi()
    {
        if (m_StatusText == null)
        {
            return;
        }

        if (Net == null)
        {
            m_StatusText.text = "网络管理器未初始化";
            return;
        }

        bool listening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        int count = listening && NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClients.Count : 0;
        bool idle = Net.State == RoomState.Idle || Net.State == RoomState.Error;

        m_StatusText.text = Net.State == RoomState.Error ? Net.LastError : GetStateText(Net.State, isHost);
        m_RoomCodeText.text = string.IsNullOrEmpty(Net.JoinCode) ? "房间码：-" : "房间码：" + Net.JoinCode;
        m_PlayerCountText.text = string.Format("{0}/{1} 玩家", count, GameNetworkManager.MaxPlayers);
        m_HostButton.interactable = idle && !m_RequestInFlight;
        m_JoinButton.interactable = idle && !m_RequestInFlight;
        m_RoomCodeInput.interactable = idle && !m_RequestInFlight;

        bool canStart = isHost && Net.State == RoomState.ReadyToStart;
        m_StartButton.gameObject.SetActive(canStart);
        m_StartButton.interactable = canStart;
    }

    private void ShowDisconnectMessage(string message)
    {
        if (m_StatusText != null)
        {
            m_StatusText.text = message;
        }
    }

    private static string GetStateText(RoomState state, bool isHost)
    {
        switch (state)
        {
            case RoomState.Idle: return "请选择创建或加入房间";
            case RoomState.Creating: return "正在连接 Unity 服务…";
            case RoomState.WaitingForPlayer: return isHost ? "房间已创建，等待玩家加入" : "正在连接主机…";
            case RoomState.PlayerJoined: return isHost ? "玩家已加入，等待开始" : "连接成功，等待房主开始";
            case RoomState.ReadyToStart: return "两名玩家已就绪";
            case RoomState.Starting: return "正在开始游戏…";
            case RoomState.LoadingGame: return "正在加载游戏场景…";
            default: return state.ToString();
        }
    }

    private static Image CreateImage(string name, Transform parent, Vector2 size, Vector2 anchor, Color color)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        var rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        imageObject.GetComponent<Image>().color = color;
        return imageObject.GetComponent<Image>();
    }

    private static Text CreateText(string name, Transform parent, string value, int size, Color color, Vector2 position, Vector2 dimensions, TextAnchor alignment)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = dimensions;
        rect.anchoredPosition = position;
        var text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static Button CreateButton(string name, Transform parent, string label, Vector2 position)
    {
        var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(280f, 62f);
        rect.anchoredPosition = position;
        var image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.12f, 0.34f, 0.72f, 1f);
        var button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        CreateText("Label", buttonObject.transform, label, 24, Color.white, Vector2.zero, new Vector2(270f, 58f), TextAnchor.MiddleCenter);
        return button;
    }

    private static InputField CreateInput(string name, Transform parent, Vector2 position)
    {
        var inputObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
        inputObject.transform.SetParent(parent, false);
        var rect = inputObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(520f, 58f);
        rect.anchoredPosition = position;
        inputObject.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.19f, 1f);
        var input = inputObject.GetComponent<InputField>();
        var text = CreateText("Text", inputObject.transform, string.Empty, 22, Color.white, Vector2.zero, new Vector2(480f, 50f), TextAnchor.MiddleLeft);
        var placeholder = CreateText("Placeholder", inputObject.transform, "输入房间码", 20, new Color(0.55f, 0.58f, 0.65f), Vector2.zero, new Vector2(480f, 50f), TextAnchor.MiddleLeft);
        input.textComponent = text;
        input.placeholder = placeholder;
        return input;
    }
}
