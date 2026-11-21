using System;
using GameNet.Gameplay;
using GameNet.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameNet.Audio
{
    /// <summary>
    /// 全局 BGM 导演：自动创建、跨场景常驻，根据场景与游戏事件切换四段音乐。
    /// 无需在场景里手动挂载，也无需改任何现有脚本。
    ///
    /// 曲库（Resources/Audio/BGM/）：
    ///   menu_bgm        主菜单 / 关卡选择 / 联机
    ///   game_bgm        关卡内基础（潜行层）
    ///   game_burst_bgm  关卡内房主怒气进入 Angry/Furious 时切到整蛊爆发层
    ///   victory_bgm     全部任务完成 → 胜利（单次播放）
    ///   defeat_sting    倒计时归零 → 失败提示音（单次播放）
    ///
    /// 音量跟随 GameAudioSettings.Music（设置面板的音乐滑条）。
    /// 注意：项目根目录有个空的 AudioSource.cs 会遮蔽 Unity 内置类型，
    /// 因此下面一律用 UnityEngine.AudioSource 全限定名。
    /// </summary>
    public sealed class GameBgmDirector : MonoBehaviour
    {
        private const string MenuPath = "Audio/BGM/menu_bgm";
        private const string GamePath = "Audio/BGM/game_bgm";
        private const string BurstPath = "Audio/BGM/game_burst_bgm";
        private const string VictoryPath = "Audio/BGM/victory_bgm";
        private const string DefeatPath = "Audio/BGM/defeat_sting";

        private const float FadeSeconds = 0.8f;

        private static GameBgmDirector s_Instance;

        private UnityEngine.AudioSource m_Source;
        private AudioClip m_Menu, m_Game, m_Burst, m_Victory, m_Defeat;

        private CountdownSystem m_Countdown;
        private LevelTaskSystem m_Tasks;
        private HomeownerAngerSystem m_Anger;

        private enum BgmKind { None, Menu, Game, Burst, Victory, Defeat }
        private BgmKind m_Current = BgmKind.None;
        private bool m_OutcomeLocked;   // 胜利/失败已触发后，不再被场景状态改回去
        private bool m_BurstActive;
        private Coroutine m_FadeRoutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (s_Instance != null) return;

            var go = new GameObject("[GameBgmDirector]");
            s_Instance = go.AddComponent<GameBgmDirector>();
            DontDestroyOnLoad(go);

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (s_Instance != null) s_Instance.ApplyForScene(scene.name);
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;
            DontDestroyOnLoad(gameObject);

            m_Source = gameObject.AddComponent<UnityEngine.AudioSource>();
            m_Source.playOnAwake = false;
            m_Source.loop = true;
            m_Source.volume = GameAudioSettings.Music;

            m_Menu = Load(MenuPath);
            m_Game = Load(GamePath);
            m_Burst = Load(BurstPath);
            m_Victory = Load(VictoryPath);
            m_Defeat = Load(DefeatPath);

            GameAudioSettings.Changed -= OnVolumeChanged;
            GameAudioSettings.Changed += OnVolumeChanged;

            // 首场景的 sceneLoaded 可能早于 Bootstrap 订阅，这里主动补一次。
            ApplyForScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GameAudioSettings.Changed -= OnVolumeChanged;
            UnbindSceneSystems();
            if (s_Instance == this) s_Instance = null;
        }

        private static AudioClip Load(string path)
        {
            AudioClip clip = Resources.Load<AudioClip>(path);
            if (clip == null)
                Debug.LogWarning("[GameBgmDirector] 找不到音频资源：" + path + "（请检查 Resources/Audio/BGM/）");
            return clip;
        }

        private void OnVolumeChanged()
        {
            if (m_Source != null) m_Source.volume = GameAudioSettings.Music;
        }

        // ------------------------------------------------------------------
        // 场景切换
        // ------------------------------------------------------------------
        private void ApplyForScene(string sceneName)
        {
            UnbindSceneSystems();
            m_OutcomeLocked = false;
            m_BurstActive = false;

            bool inLevel = !IsMenuScene(sceneName);

            if (inLevel)
            {
                // 关卡内：先绑事件，再决定起始曲（游戏本身会写 m_Current）
                BindSceneSystems();
                SwitchTo(BgmKind.Game, loop: true);
            }
            else
            {
                SwitchTo(BgmKind.Menu, loop: true);
            }
        }

        private static bool IsMenuScene(string name)
        {
            return string.Equals(name, "主菜单", StringComparison.Ordinal)
                || string.Equals(name, "关卡选择", StringComparison.Ordinal)
                || string.Equals(name, "联机", StringComparison.Ordinal);
        }

        private void BindSceneSystems()
        {
            m_Countdown = FindObjectOfType<CountdownSystem>();
            m_Tasks = FindObjectOfType<LevelTaskSystem>();
            m_Anger = FindObjectOfType<HomeownerAngerSystem>();

            if (m_Countdown != null) m_Countdown.OnTimeUp += HandleTimeUp;
            if (m_Tasks != null) m_Tasks.OnAllTasksCompleted += HandleAllTasksCompleted;
            if (m_Anger != null) m_Anger.OnAngerStageChanged += HandleAngerStageChanged;
        }

        private void UnbindSceneSystems()
        {
            if (m_Countdown != null) m_Countdown.OnTimeUp -= HandleTimeUp;
            if (m_Tasks != null) m_Tasks.OnAllTasksCompleted -= HandleAllTasksCompleted;
            if (m_Anger != null) m_Anger.OnAngerStageChanged -= HandleAngerStageChanged;
            m_Countdown = null;
            m_Tasks = null;
            m_Anger = null;
        }

        // ------------------------------------------------------------------
        // 游戏事件
        // ------------------------------------------------------------------
        public void NotifyPlayerCaught() => HandleTimeUp();

        private void HandleTimeUp()
        {
            if (m_OutcomeLocked) return;
            m_OutcomeLocked = true;
            SwitchTo(BgmKind.Defeat, loop: false);
        }

        private void HandleAllTasksCompleted()
        {
            if (m_OutcomeLocked) return;
            m_OutcomeLocked = true;
            SwitchTo(BgmKind.Victory, loop: false);
        }

        private void HandleAngerStageChanged(AngerStage previous, AngerStage current)
        {
            if (m_OutcomeLocked) return;

            bool shouldBurst = current == AngerStage.Angry || current == AngerStage.Furious;
            if (shouldBurst == m_BurstActive) return;
            m_BurstActive = shouldBurst;
            SwitchTo(shouldBurst ? BgmKind.Burst : BgmKind.Game, loop: true);
        }

        private void Update()
        {
            // 兜底：联机客户端的 CountdownSystem 不一定抛 OnTimeUp，这里用 NetworkVariable 状态补判。
            if (!m_OutcomeLocked && m_Countdown != null && m_Countdown.DurationSeconds > 0f
                && !m_Countdown.IsRunning && m_Countdown.RemainingSeconds <= 0f)
            {
                HandleTimeUp();
            }

            // 兜底：若任务系统在 BGM 导演绑定前就完成了全部任务。
            if (!m_OutcomeLocked && m_Tasks != null && m_Tasks.AreAllTasksCompleted())
            {
                HandleAllTasksCompleted();
            }
        }

        // ------------------------------------------------------------------
        // 播放
        // ------------------------------------------------------------------
        private AudioClip ClipFor(BgmKind kind)
        {
            switch (kind)
            {
                case BgmKind.Menu: return m_Menu;
                case BgmKind.Game: return m_Game;
                case BgmKind.Burst: return m_Burst;
                case BgmKind.Victory: return m_Victory;
                case BgmKind.Defeat: return m_Defeat;
                default: return null;
            }
        }

        private void SwitchTo(BgmKind kind, bool loop)
        {
            AudioClip clip = ClipFor(kind);
            if (clip == null) return;
            if (m_Current == kind && m_Source.isPlaying && m_Source.loop == loop) return;

            m_Current = kind;
            if (m_FadeRoutine != null) StopCoroutine(m_FadeRoutine);
            m_FadeRoutine = StartCoroutine(CrossFade(clip, loop));
        }

        private System.Collections.IEnumerator CrossFade(AudioClip clip, bool loop)
        {
            // 用 unscaledDeltaTime：失败面板把 Time.timeScale 设为 0 时仍能淡入淡出。
            float from = m_Source.volume;
            float target = GameAudioSettings.Music;

            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                m_Source.volume = Mathf.Lerp(from, 0f, t / FadeSeconds);
                yield return null;
            }

            m_Source.volume = 0f;
            m_Source.clip = clip;
            m_Source.loop = loop;
            m_Source.Play();

            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                m_Source.volume = Mathf.Lerp(0f, target, t / FadeSeconds);
                yield return null;
            }

            m_Source.volume = target;
            m_FadeRoutine = null;
        }
    }
}
