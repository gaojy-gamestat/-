using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// GamePlay 中只负责把关卡配置交给三个逻辑系统，不包含 UI。
    /// 注意：倒计时时长以场景里 CountdownSystem 组件 Inspector 上的设置为准，
    /// 这里不再用 LevelConfig 覆盖，避免"改了组件上的数字却没反应"。
    /// </summary>
    public sealed class GameplayProgressRoot : MonoBehaviour
    {
        [SerializeField] private LevelConfig levelConfig;
        [SerializeField] private HomeownerAngerSystem angerSystem;
        [SerializeField] private LevelTaskSystem taskSystem;
        [SerializeField] private CountdownSystem countdownSystem;

        public LevelConfig Config => levelConfig;
        public HomeownerAngerSystem Anger => angerSystem;
        public LevelTaskSystem Tasks => taskSystem;
        public CountdownSystem Countdown => countdownSystem;

        public void Configure(LevelConfig config)
        {
            levelConfig = config;
            if (taskSystem != null) taskSystem.SetLevelConfig(config);
        }

        private void Awake()
        {
            angerSystem = angerSystem != null ? angerSystem : GetComponent<HomeownerAngerSystem>();
            taskSystem = taskSystem != null ? taskSystem : GetComponent<LevelTaskSystem>();
            countdownSystem = countdownSystem != null ? countdownSystem : GetComponent<CountdownSystem>();

            if (taskSystem != null)
            {
                taskSystem.SetLevelConfig(levelConfig);
                taskSystem.InitializeFromConfig();
            }
        }
    }
}
