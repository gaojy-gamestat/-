using System.Collections.Generic;
using UnityEngine;

namespace GameNet.Gameplay
{
    [CreateAssetMenu(menuName = "GameNet/Level Config", fileName = "LevelConfig")]
    public sealed class LevelConfig : ScriptableObject
    {
        public int levelId = 1;
        public string levelDisplayName = "第一关";

        [Tooltip("已不再用于倒计时：每关时长请改场景里 CountdownSystem 组件上的「倒计时时长（秒）」。")]
        [Min(0f)] public float timeLimitSeconds = 600f;

        public List<LevelTaskData> tasks = new List<LevelTaskData>();
    }
}
