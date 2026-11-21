using System.Collections.Generic;
using GameNet.Audio;
using GameNet.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    [RequireComponent(typeof(DukePatroller))]
    [DisallowMultipleComponent]
    public sealed class Level01DukeGameplay : MonoBehaviour
    {
        public DukePatroller duke;
        public HomeownerAngerSystem anger;
        public LevelTaskSystem tasks;
        public CountdownSystem countdown;
        public TMP_FontAsset font;
        [Header("整蛊与平复（怒气点数）")]
        [Min(0)] public float prankAnger = 35f;
        [Min(0)] public float eatCalmPerSecond = 0.5f;
        [Min(0)] public float sleepCalmPerSecond = 1f;
        public string[] prankTaskIds = { "prank" };
        [Header("厨房餐桌整蛊点（图像坐标）")]
        public Vector2 prankPosition = new Vector2(.847f, .785f);
        [Min(.001f)] public float interactRadius = .035f;
        [Header("接触判定（图像坐标，横纵半径）")]
        public Vector2 contactRadius = new Vector2(.024f, .022f);
        public bool HasFailed { get; private set; }
        public bool KitchenPrankCompleted => completedPranks.Contains("kitchen_table");
        readonly HashSet<string> completedPranks = new HashSet<string>();
        VillaCharacterWalker player;
        VillaWall[] walls;
        TMP_Text prompt;
        Canvas promptCanvas;
        float feedbackUntil;
        bool HasAuthority => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;

        void Start()
        {
            if (duke == null) duke = GetComponent<DukePatroller>();
            if (anger == null) anger = FindObjectOfType<HomeownerAngerSystem>();
            if (tasks == null) tasks = FindObjectOfType<LevelTaskSystem>();
            if (countdown == null) countdown = FindObjectOfType<CountdownSystem>();
            player = FindObjectOfType<VillaCharacterWalker>();
            if (duke.room == null) duke.room = FindObjectOfType<VillaRoomBuilder>();
            walls = duke.room != null ? duke.room.GetComponentsInChildren<VillaWall>(true) : new VillaWall[0];
            if (tasks != null) tasks.OnTaskCompleted += OnTaskCompleted;
            BuildPrompt();
        }

        void OnDestroy()
        {
            if (tasks != null) tasks.OnTaskCompleted -= OnTaskCompleted;
            if (promptCanvas != null) Destroy(promptCanvas.gameObject);
        }

        void OnTaskCompleted(LevelTaskData task)
        {
            if (System.Array.IndexOf(prankTaskIds, task.taskId) >= 0) ReportPrankCompleted(task.taskId);
        }

        public bool ReportPrankCompleted(string prankId)
        {
            if (Time.timeScale <= 0f || !HasAuthority || HasFailed || anger == null || string.IsNullOrWhiteSpace(prankId)) return false;
            if (countdown != null && !countdown.IsRunning) return false;
            if (!completedPranks.Add(prankId)) return false;
            anger.AddAnger(prankAnger);
            feedbackUntil = Time.unscaledTime + 3f;
            if (prompt != null) prompt.text = $"整蛊成功！Duke 怒气 +{prankAnger:0}";
            return true;
        }

        void Update()
        {
            if (Time.timeScale <= 0f || duke == null || !duke.IsReady || HasFailed || !HasAuthority) return;
            if (countdown != null && !countdown.IsRunning) return;
            if (tasks != null && tasks.AreAllTasksCompleted()) return;
            if (anger != null && duke.IsActing)
            {
                float calm = duke.CurrentActivity == DukePatroller.DukeAction.Eat ? eatCalmPerSecond
                    : duke.CurrentActivity == DukePatroller.DukeAction.Sleep ? sleepCalmPerSecond : 0f;
                anger.RemoveAnger(calm * Time.deltaTime);
            }
            if (player == null) return;
            Vector2 p = duke.WorldToArtworkNorm(player.FeetWorldPosition);
            if (CanCatchAt(p)) { CatchPlayer(); return; }
            bool nearTable = Vector2.Distance(p, prankPosition) <= interactRadius
                && SharesFloor(p, prankPosition);
            if (Time.unscaledTime >= feedbackUntil && prompt != null)
                prompt.text = nearTable ? KitchenPrankCompleted ? "这里已经动过手脚了" : "按 E 在餐桌上动手脚" : "";
            if (nearTable && Input.GetKeyDown(KeyCode.E)) TryKitchenPrank();
        }

        public bool TryKitchenPrank()
        {
            if (Time.timeScale <= 0f || player == null || duke == null || !duke.IsReady) return false;
            if (tasks != null && tasks.AreAllTasksCompleted()) return false;
            Vector2 p = duke.WorldToArtworkNorm(player.FeetWorldPosition);
            if (Vector2.Distance(p, prankPosition) > interactRadius || !SharesFloor(p, prankPosition)) return false;
            return ReportPrankCompleted("kitchen_table");
        }

        bool SharesFloor(Vector2 a, Vector2 b)
        {
            if (duke.room == null || !duke.room.IsReady) return false;
            foreach (var floor in duke.room.Floors)
                if (floor != null && floor.isActiveAndEnabled && floor.ContainsImageNorm(a) && floor.ContainsImageNorm(b)) return true;
            return false;
        }

        public bool CanCatchAt(Vector2 playerPoint)
        {
            if (duke == null || !duke.IsReady || !SharesFloor(playerPoint, duke.ImageNorm)) return false;
            Vector2 contact = duke.EncounterImageNorm;
            Vector2 d = playerPoint - contact;
            float x = d.x / Mathf.Max(.001f, contactRadius.x);
            float y = d.y / Mathf.Max(.001f, contactRadius.y);
            if (x * x + y * y > 1f) return false;
            // 用墙体区域检查视线；坐下/躺下的身体允许位于对应家具上。
            int samples = Mathf.Max(2, Mathf.CeilToInt(d.magnitude / .001f));
            foreach (var wall in walls)
            {
                if (wall == null || !wall.isActiveAndEnabled) continue;
                for (int i = 0; i <= samples; i++)
                    if (wall.ContainsImageNorm(Vector2.Lerp(contact, playerPoint, (float)i / samples))) return false;
            }
            return true;
        }

        void CatchPlayer()
        {
            if (HasFailed) return;
            HasFailed = true;
            if (countdown != null) countdown.PauseTimer();
            duke.SuspendPatrol();
            if (player != null) player.SuspendMovement();
            if (prompt != null) prompt.text = "";
            var bgm = FindObjectOfType<GameBgmDirector>();
            if (bgm != null) bgm.NotifyPlayerCaught();
            TimeUpPanelRuntime.ShowCaught(font);
        }

        void BuildPrompt()
        {
            var go = new GameObject("DukeInteractionHint", typeof(Canvas), typeof(CanvasScaler));
            promptCanvas = go.GetComponent<Canvas>();
            promptCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            promptCanvas.sortingOrder = 20;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var label = new GameObject("Hint", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(go.transform, false);
            prompt = label.GetComponent<TextMeshProUGUI>();
            prompt.font = font;
            prompt.fontSize = 32;
            prompt.alignment = TextAlignmentOptions.Center;
            prompt.raycastTarget = false;
            prompt.rectTransform.anchorMin = prompt.rectTransform.anchorMax = new Vector2(.5f, 0);
            prompt.rectTransform.sizeDelta = new Vector2(1000, 70);
            prompt.rectTransform.anchoredPosition = new Vector2(0, 170);
            prompt.text = "";
        }
    }
}
