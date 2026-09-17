using System;
using UnityEngine;

namespace GameNet.Gameplay
{
    [Serializable]
    public class LevelTaskData
    {
        public string taskId;
        public string description;
        public int currentProgress;
        public int targetProgress = 1;

        public bool IsCompleted => targetProgress > 0 && currentProgress >= targetProgress;

        public LevelTaskData Clone()
        {
            return new LevelTaskData
            {
                taskId = taskId,
                description = description,
                currentProgress = Mathf.Max(0, currentProgress),
                targetProgress = Mathf.Max(1, targetProgress)
            };
        }
    }
}
