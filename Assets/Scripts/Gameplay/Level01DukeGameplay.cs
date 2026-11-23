using GameNet.Audio;
using GameNet.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine;

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
        [Header("平复（怒气点数 / 秒）")]
        [Min(0)] public float eatCalmPerSecond = 0.5f;
        [Min(0)] public float sleepCalmPerSecond = 1f;
        [Header("接触判定（图像坐标，横纵半径）")]
        public Vector2 contactRadius = new Vector2(.024f, .022f);
        public bool HasFailed { get; private set; }
        VillaCharacterWalker[] players;
        VillaWall[] walls;
        bool HasAuthority => NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;

        void Start()
        {
            if (duke == null) duke = GetComponent<DukePatroller>();
            if (anger == null) anger = FindObjectOfType<HomeownerAngerSystem>();
            if (tasks == null) tasks = FindObjectOfType<LevelTaskSystem>();
            if (countdown == null) countdown = FindObjectOfType<CountdownSystem>();
            players = FindObjectsOfType<VillaCharacterWalker>();
            if (duke.room == null) duke.room = FindObjectOfType<VillaRoomBuilder>();
            walls = duke.room != null ? duke.room.GetComponentsInChildren<VillaWall>(true) : new VillaWall[0];
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
            // 发现判定：走 VillaRoomBuilder 的区域管理接口 InSameRoom——
            // 优先按人工摆放的房间区域（VillaRoomArea）判定，一个区域可罩住多块拼接地板；
            // 没摆放房间区域时退化为“同一块地板”。任一玩家与房主同处一室即播失败流程。
            foreach (var player in players)
            {
                if (player == null || duke.room == null) continue;
                if (duke.room.InSameRoom(player.FeetWorldPosition, duke.transform.position))
                {
                    Debug.Log($"[被发现] {player.gameObject.name} 与房主同处一室（{duke.room.RoomNameAt(player.FeetWorldPosition)}）");
                    CatchPlayer();
                    return;
                }
            }
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
            foreach (var player in players)
                if (player != null) player.SuspendMovement();
            var bgm = FindObjectOfType<GameBgmDirector>();
            if (bgm != null) bgm.NotifyPlayerCaught();
            // 先播"被发现"失败过场视频，播完再弹重新挑战/返回选关面板
            GameNet.UI.VideoCutscenePlayer.Play("视频/被发现失败", null,
                () => TimeUpPanelRuntime.ShowCaught(font));
        }

    }
}
