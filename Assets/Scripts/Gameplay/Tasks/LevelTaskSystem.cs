using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GameNet.Gameplay
{
    public struct TaskProgressState : INetworkSerializable, IEquatable<TaskProgressState>
    {
        public int taskIndex;
        public int currentProgress;
        public int targetProgress;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref taskIndex);
            serializer.SerializeValue(ref currentProgress);
            serializer.SerializeValue(ref targetProgress);
        }

        public bool Equals(TaskProgressState other)
        {
            return taskIndex == other.taskIndex && currentProgress == other.currentProgress && targetProgress == other.targetProgress;
        }
    }

    /// <summary>任务真实进度的唯一数据源。联机时只有 Host/Server 能修改 NetworkList。</summary>
    public sealed class LevelTaskSystem : NetworkBehaviour
    {
        [SerializeField] private LevelConfig levelConfig;

        private readonly NetworkList<TaskProgressState> m_NetworkTasks = new NetworkList<TaskProgressState>();
        private readonly List<LevelTaskData> m_LocalTasks = new List<LevelTaskData>();
        private readonly Dictionary<string, bool> m_LastCompleted = new Dictionary<string, bool>(StringComparer.Ordinal);
        private bool m_AllTasksCompleted;

        public event Action<LevelTaskData> OnTaskProgressChanged;
        public event Action<LevelTaskData> OnTaskCompleted;
        public event Action<float> OnOverallProgressChanged;
        public event Action OnAllTasksCompleted;

        public LevelConfig Config => levelConfig;
        private bool IsNetworkStateActive => IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool HasAuthority => !IsNetworkStateActive || IsServer;

        public void SetLevelConfig(LevelConfig config)
        {
            if (IsNetworkStateActive && !IsServer) return;
            levelConfig = config;
        }

        public void InitializeFromConfig()
        {
            InitializeTasks(levelConfig != null ? levelConfig.tasks : new List<LevelTaskData>());
        }

        public void InitializeTasks(IEnumerable<LevelTaskData> definitions)
        {
            if (!HasAuthority) return;

            var clones = CloneTasks(definitions);
            m_LastCompleted.Clear();
            m_AllTasksCompleted = false;
            if (IsNetworkStateActive)
            {
                m_NetworkTasks.Clear();
                foreach (var task in clones)
                    m_NetworkTasks.Add(ToNetworkState(task));
                return;
            }

            m_LocalTasks.Clear();
            m_LocalTasks.AddRange(clones);
            PublishAllTaskState();
        }

        public void SetTaskProgress(string taskId, int value)
        {
            if (!HasAuthority || string.IsNullOrWhiteSpace(taskId)) return;
            int index = FindTaskIndex(taskId);
            if (index < 0) return;

            LevelTaskData task = GetTaskAt(index);
            int next = Mathf.Clamp(value, 0, Mathf.Max(1, task.targetProgress));
            if (task.currentProgress == next) return;
            task.currentProgress = next;
            WriteTask(index, task);
        }

        public void AddTaskProgress(string taskId, int amount = 1)
        {
            if (!HasAuthority || amount <= 0) return;
            LevelTaskData task = GetTask(taskId);
            if (task == null) return;
            SetTaskProgress(taskId, task.currentProgress + amount);
        }

        public void RemoveTaskProgress(string taskId, int amount = 1)
        {
            if (!HasAuthority || amount <= 0) return;
            LevelTaskData task = GetTask(taskId);
            if (task == null) return;
            SetTaskProgress(taskId, task.currentProgress - amount);
        }

        public void CompleteTask(string taskId)
        {
            if (!HasAuthority) return;
            LevelTaskData task = GetTask(taskId);
            if (task == null) return;
            SetTaskProgress(taskId, Mathf.Max(1, task.targetProgress));
        }

        public void ResetTasks()
        {
            InitializeFromConfig();
        }

        public LevelTaskData GetTask(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return null;
            int index = FindTaskIndex(taskId);
            return index < 0 ? null : GetTaskAt(index);
        }

        public List<LevelTaskData> GetAllTasks()
        {
            var result = new List<LevelTaskData>();
            int count = IsNetworkStateActive ? m_NetworkTasks.Count : m_LocalTasks.Count;
            for (int i = 0; i < count; i++) result.Add(GetTaskAt(i));
            return result;
        }

        public int GetCompletedTaskCount()
        {
            int completed = 0;
            int count = IsNetworkStateActive ? m_NetworkTasks.Count : m_LocalTasks.Count;
            for (int i = 0; i < count; i++) if (GetTaskAt(i).IsCompleted) completed++;
            return completed;
        }

        public int GetTotalTaskCount() => IsNetworkStateActive ? m_NetworkTasks.Count : m_LocalTasks.Count;

        public float GetOverallProgress()
        {
            int total = GetTotalTaskCount();
            return total == 0 ? 0f : Mathf.Clamp01((float)GetCompletedTaskCount() / total);
        }

        public bool AreAllTasksCompleted()
        {
            return GetTotalTaskCount() > 0 && GetCompletedTaskCount() == GetTotalTaskCount();
        }

        public override void OnNetworkSpawn()
        {
            m_NetworkTasks.OnListChanged += HandleNetworkTasksChanged;
            if (IsServer) InitializeFromConfig();
            else PublishAllTaskState();
        }

        public override void OnNetworkDespawn()
        {
            m_NetworkTasks.OnListChanged -= HandleNetworkTasksChanged;
        }

        private void HandleNetworkTasksChanged(NetworkListEvent<TaskProgressState> changeEvent)
        {
            PublishAllTaskState();
        }

        private void PublishAllTaskState()
        {
            var tasks = GetAllTasks();
            foreach (var task in tasks)
            {
                bool wasCompleted = m_LastCompleted.TryGetValue(task.taskId ?? string.Empty, out bool previous) && previous;
                OnTaskProgressChanged?.Invoke(task);
                if (task.IsCompleted && !wasCompleted) OnTaskCompleted?.Invoke(task);
                m_LastCompleted[task.taskId ?? string.Empty] = task.IsCompleted;
            }

            float overall = GetOverallProgress();
            OnOverallProgressChanged?.Invoke(overall);
            bool allCompleted = AreAllTasksCompleted();
            if (allCompleted && !m_AllTasksCompleted) OnAllTasksCompleted?.Invoke();
            m_AllTasksCompleted = allCompleted;
        }

        private void WriteTask(int index, LevelTaskData task)
        {
            if (IsNetworkStateActive)
            {
                m_NetworkTasks[index] = ToNetworkState(task);
            }
            else
            {
                m_LocalTasks[index] = task;
                PublishAllTaskState();
            }
        }

        private int FindTaskIndex(string taskId)
        {
            int count = IsNetworkStateActive ? m_NetworkTasks.Count : m_LocalTasks.Count;
            for (int i = 0; i < count; i++)
            {
                string id = GetTaskAt(i).taskId;
                if (string.Equals(id, taskId, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private LevelTaskData GetTaskAt(int index)
        {
            if (IsNetworkStateActive)
            {
                var state = m_NetworkTasks[index];
                return new LevelTaskData
                {
                    taskId = FindTaskId(state.taskIndex),
                    description = FindDescription(FindTaskId(state.taskIndex)),
                    currentProgress = Mathf.Max(0, state.currentProgress),
                    targetProgress = Mathf.Max(1, state.targetProgress)
                };
            }

            return m_LocalTasks[index].Clone();
        }

        private string FindDescription(string taskId)
        {
            if (levelConfig == null || levelConfig.tasks == null) return taskId;
            foreach (var task in levelConfig.tasks)
                if (task != null && task.taskId == taskId) return task.description;
            return taskId;
        }

        private string FindTaskId(int taskIndex)
        {
            if (levelConfig != null && levelConfig.tasks != null && taskIndex >= 0 && taskIndex < levelConfig.tasks.Count)
                return levelConfig.tasks[taskIndex].taskId;
            return "task_" + taskIndex;
        }

        private static List<LevelTaskData> CloneTasks(IEnumerable<LevelTaskData> definitions)
        {
            var result = new List<LevelTaskData>();
            if (definitions == null) return result;
            foreach (var task in definitions)
            {
                if (task == null || string.IsNullOrWhiteSpace(task.taskId)) continue;
                result.Add(task.Clone());
            }
            return result;
        }

        private TaskProgressState ToNetworkState(LevelTaskData task)
        {
            return new TaskProgressState
            {
                taskIndex = FindDefinitionIndex(task.taskId),
                currentProgress = Mathf.Max(0, task.currentProgress),
                targetProgress = Mathf.Max(1, task.targetProgress)
            };
        }

        private int FindDefinitionIndex(string taskId)
        {
            if (levelConfig != null && levelConfig.tasks != null)
            {
                for (int i = 0; i < levelConfig.tasks.Count; i++)
                    if (levelConfig.tasks[i] != null && levelConfig.tasks[i].taskId == taskId) return i;
            }
            return 0;
        }
    }
}
