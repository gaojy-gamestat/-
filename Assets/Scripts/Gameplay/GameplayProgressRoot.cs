using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>GamePlay 中只负责把关卡配置交给三个逻辑系统，不包含 UI。</summary>
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
            if (countdownSystem != null) countdownSystem.SetDuration(config != null ? config.timeLimitSeconds : 0f);
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

            if (countdownSystem != null)
                countdownSystem.SetDuration(levelConfig != null ? levelConfig.timeLimitSeconds : 0f);
        }
    }
}
