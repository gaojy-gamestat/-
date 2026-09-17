using System.Collections.Generic;
using UnityEngine;

namespace GameNet.Gameplay
{
    [CreateAssetMenu(menuName = "GameNet/Level Config", fileName = "LevelConfig")]
    public sealed class LevelConfig : ScriptableObject
    {
        public int levelId = 1;
        public string levelDisplayName = "第一关";
        [Min(0f)] public float timeLimitSeconds = 600f;
        public List<LevelTaskData> tasks = new List<LevelTaskData>();
    }
}
