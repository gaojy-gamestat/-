using System.Collections;
using System.Collections.Generic;
using GameNet;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SampleScene 原主菜单的联机入口。
/// 原场景的前两个按钮负责进入创建/加入流程，本脚本只在点击后显示安全弹窗。
/// </summary>
public sealed class SampleSceneMenuRuntime : MonoBehaviour
{
    private enum MenuMode
    {
        None,
        Create,
        Join
    }

    private Canvas m_Canvas;
    private Text m_Title;
    private Text m_Status;
    private Text m_RoomCode;
    private Text m_PlayerCount;
    private InputField m_CodeInput;
    private GameObject m_SaveList;
    private Button m_JoinButton;
    private Button m_StartGameButton;
    private Button m_CloseButton;
    private MenuMode m_Mode;
    private bool m_RequestInFlight;
    private float m_NextRefreshTime;
    private GameSaveData m_SelectedSession;

    private GameNetworkManager Net => GameNetworkManager.Instance;

    private void Start()
    {
        BuildUi();
        m_Canvas.enabled = false;
        if (Net != null)
        {
            Net.OnRoomStateChanged += RefreshUi;
            Net.OnDisconnectMessage += ShowDisconnectMessage;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime >= m_NextRefreshTime)
        {
            m_NextRefreshTime = Time.unscaledTime + 0.25f;
            RefreshUi();
        }
    }

    private void OnDestroy()
    {
        if (Net != null)
        {
            Net.OnRoomStateChanged -= RefreshUi;
            Net.OnDisconnectMessage -= ShowDisconnectMessage;
        }
    }

    public void OpenCreateFromOriginalButton()
    {
        m_Canvas.enabled = true;
        m_Mode = MenuMode.Create;
        m_SelectedSession = null;
        RenderCreateMenu();
    }

    public void OpenJoinFromOriginalButton()
    {
        m_Canvas.enabled = true;
        m_Mode = MenuMode.Join;
        RenderJoinMenu();
    }

    private void RenderCreateMenu()
    {
        m_Title.text = "创建游戏";
        m_Status.text = "正在读取存档…";
        m_RoomCode.text = "房间码：-";
        m_CodeInput.gameObject.SetActive(false);
        m_JoinButton.gameObject.SetActive(false);
        m_StartGameButton.gameObject.SetActive(false);
        m_SaveList.SetActive(true);
        ClearSaveButtons();

        List<string> saves = SaveSystem.LoadAllSaveFiles();
        if (saves.Count == 0)
        {
            m_Status.text = "没有存档，请选择创建新游戏";
        }
        else
        {
            m_Status.text = "请选择读取存档，或创建新游戏";
        }

        AddSaveButton("创建新游戏", string.Empty, 0);
        for (int i = 0; i < saves.Count; i++)
        {
            AddSaveButton("读取存档：" + saves[i], saves[i], i + 1);
        }
    }

    private void RenderJoinMenu()
    {
        m_Title.text = "加入游戏";
        m_Status.text = "请输入房间码，然后加入房主的房间";
        m_RoomCode.text = "房间码：-";
        m_SaveList.SetActive(false);
        m_CodeInput.gameObject.SetActive(true);
        m_JoinButton.gameObject.SetActive(true);
        m_StartGameButton.gameObject.SetActive(false);
        m_CodeInput.text = string.Empty;
        m_CodeInput.Select();
        m_CodeInput.ActivateInputField();
    }

    private void AddSaveButton(string label, string slotName, int index)
    {
        var button = CreateButton("SaveOption_" + index, m_SaveList.transform, label, new Vector2(0f, 95f - index * 62f));
        button.onClick.AddListener(() => SelectSave(slotName));
    }

    private void SelectSave(string slotName)
    {
        if (m_RequestInFlight)
        {
            return;
        }

        if (string.IsNullOrEmpty(slotName))
        {
            m_SelectedSession = new GameSaveData
            {
                saveName = "新游戏",
                sceneName = GameNetworkManager.GamePlaySceneName,
                chapterIndex = 0
            };
        }
        else
        {
            m_SelectedSession = SaveSystem.LoadOneSave(slotName);
            if (m_SelectedSession == null)
            {
                m_Status.text = "读取存档失败，请重新选择";
                return;
            }
        }

        m_Status.text = string.IsNullOrEmpty(slotName)
            ? "已选择新游戏，正在生成房间码…"
            : "已读取存档：" + slotName + "，正在生成房间码…";
        StartCoroutine(StartHostRoutine());
    }

    private IEnumerator StartHostRoutine()
    {
        m_RequestInFlight = true;
        var task = Net != null ? Net.StartHostAsync() : null;
        if (task != null)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
        }

        m_RequestInFlight = false;
        if (Net != null && Net.State != RoomState.Error && m_SelectedSession != null)
        {
            SaveSystem.SetCurrentSession(m_SelectedSession);
        }
        RefreshUi();
    }

    private void OnJoinClicked()
    {
        if (!m_RequestInFlight)
        {
            StartCoroutine(JoinRoutine());
        }
    }

    private IEnumerator JoinRoutine()
    {
        m_RequestInFlight = true;
        var task = Net != null ? Net.JoinClientAsync(m_CodeInput.text) : null;
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

    private void OnStartGameClicked()
    {
        Net?.StartGame();
    }

    private void CloseMenu()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            m_Canvas.enabled = false;
            m_Mode = MenuMode.None;
        }
    }

    private void RefreshUi()
    {
        if (m_Status == null || Net == null)
        {
            return;
        }

        bool listening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        int count = listening ? NetworkManager.Singleton.ConnectedClients.Count : 0;
        m_RoomCode.text = string.IsNullOrEmpty(Net.JoinCode) ? "房间码：-" : "房间码：" + Net.JoinCode;
        m_PlayerCount.text = string.Format("{0}/{1} 玩家", count, GameNetworkManager.MaxPlayers);

        if (Net.State == RoomState.Error)
        {
            m_Status.text = Net.LastError;
        }
        else if (Net.State == RoomState.WaitingForPlayer && isHost)
        {
            m_Status.text = "房间已创建，等待玩家加入";
        }
        else if (Net.State == RoomState.PlayerJoined && !isHost)
        {
            m_Status.text = "已加入房间，等待房主开始";
        }
        else if (Net.State == RoomState.ReadyToStart)
        {
            m_Status.text = "两名玩家已就绪，房主可以开始游戏";
        }

        bool canStart = isHost && Net.State == RoomState.ReadyToStart;
        m_StartGameButton.gameObject.SetActive(canStart);
        m_StartGameButton.interactable = canStart;
        m_JoinButton.interactable = m_Mode == MenuMode.Join && !m_RequestInFlight && !listening;
        m_CodeInput.interactable = m_Mode == MenuMode.Join && !m_RequestInFlight && !listening;
    }

    private void ShowDisconnectMessage(string message)
    {
        if (m_Status != null)
        {
            m_Status.text = message;
        }
    }

    private void ClearSaveButtons()
    {
        for (int i = m_SaveList.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(m_SaveList.transform.GetChild(i).gameObject);
        }
    }

    private void BuildUi()
    {
        var canvasObject = new GameObject("SampleSceneNetworkCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        canvasObject.layer = 5;
        m_Canvas = canvasObject.GetComponent<Canvas>();
        m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        m_Canvas.sortingOrder = 200;

        var panel = CreateImage("NetworkPanel", m_Canvas.transform, new Vector2(780f, 650f), new Vector2(0.5f, 0.5f), new Color(0.025f, 0.035f, 0.055f, 0.98f));
        m_Title = CreateText("Title", panel.transform, "联机", 38, Color.white, new Vector2(0f, 260f), new Vector2(680f, 60f), TextAnchor.MiddleCenter);
        m_Status = CreateText("Status", panel.transform, "", 21, new Color(0.75f, 0.85f, 1f), new Vector2(0f, 205f), new Vector2(680f, 48f), TextAnchor.MiddleCenter);
        m_RoomCode = CreateText("RoomCode", panel.transform, "房间码：-", 27, new Color(1f, 0.86f, 0.35f), new Vector2(0f, 160f), new Vector2(680f, 48f), TextAnchor.MiddleCenter);
        m_PlayerCount = CreateText("PlayerCount", panel.transform, "0/2 玩家", 19, Color.white, new Vector2(0f, 125f), new Vector2(680f, 36f), TextAnchor.MiddleCenter);

        m_SaveList = new GameObject("SaveList", typeof(RectTransform));
        m_SaveList.transform.SetParent(panel.transform, false);
        var saveRect = m_SaveList.GetComponent<RectTransform>();
        saveRect.anchorMin = new Vector2(0.5f, 0.5f);
        saveRect.anchorMax = new Vector2(0.5f, 0.5f);
        saveRect.sizeDelta = new Vector2(620f, 260f);
        saveRect.anchoredPosition = new Vector2(0f, -60f);

        m_CodeInput = CreateInput("RoomCodeInput", panel.transform, new Vector2(0f, -35f));
        m_CodeInput.placeholder.GetComponent<Text>().text = "输入房间码";
        m_JoinButton = CreateButton("JoinButton", panel.transform, "加入房主房间", new Vector2(0f, -115f));
        m_JoinButton.onClick.AddListener(OnJoinClicked);
        m_StartGameButton = CreateButton("StartGameButton", panel.transform, "开始游戏", new Vector2(0f, -115f));
        m_StartGameButton.onClick.AddListener(OnStartGameClicked);
        m_CloseButton = CreateButton("CloseButton", panel.transform, "返回", new Vector2(0f, -205f));
        m_CloseButton.onClick.AddListener(CloseMenu);
    }

    private static Image CreateImage(string name, Transform parent, Vector2 size, Vector2 anchor, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        go.GetComponent<Image>().color = color;
        return go.GetComponent<Image>();
    }

    private static Text CreateText(string name, Transform parent, string value, int size, Color color, Vector2 position, Vector2 dimensions, TextAnchor alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = dimensions;
        rect.anchoredPosition = position;
        var text = go.GetComponent<Text>();
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
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(560f, 52f);
        rect.anchoredPosition = position;
        var image = go.GetComponent<Image>();
        image.color = new Color(0.12f, 0.34f, 0.72f, 1f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        CreateText("Label", go.transform, label, 22, Color.white, Vector2.zero, new Vector2(540f, 48f), TextAnchor.MiddleCenter);
        return button;
    }

    private static InputField CreateInput(string name, Transform parent, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(560f, 56f);
        rect.anchoredPosition = position;
        go.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.19f, 1f);
        var input = go.GetComponent<InputField>();
        var text = CreateText("Text", go.transform, string.Empty, 22, Color.white, Vector2.zero, new Vector2(520f, 50f), TextAnchor.MiddleLeft);
        var placeholder = CreateText("Placeholder", go.transform, "输入房间码", 20, new Color(0.55f, 0.58f, 0.65f), Vector2.zero, new Vector2(520f, 50f), TextAnchor.MiddleLeft);
        input.textComponent = text;
        input.placeholder = placeholder;
        return input;
    }
}
