using System;
using Unity.Netcode;
using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 房主怒气的唯一数据源。联机时 NetworkVariable 只允许 Server 写入；未启动联机时作为本地逻辑使用。
    /// </summary>
    public sealed class HomeownerAngerSystem : NetworkBehaviour
    {
        [SerializeField, Min(0f)] private float defaultMaxAnger = 100f;

        private readonly NetworkVariable<float> m_NetworkAnger = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> m_NetworkMaxAnger = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float m_LocalAnger;
        private float m_LocalMaxAnger;
        private AngerStage m_CurrentStage;
        private bool m_StageInitialized;

        public float CurrentAnger => IsNetworkStateActive ? Sanitize(m_NetworkAnger.Value) : m_LocalAnger;
        public float MaxAnger => IsNetworkStateActive ? SanitizeMax(m_NetworkMaxAnger.Value) : m_LocalMaxAnger;
        public event Action<float, float, float> OnAngerChanged;
        public event Action<AngerStage, AngerStage> OnAngerStageChanged;

        private bool IsNetworkStateActive => IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool HasAuthority => !IsNetworkStateActive || IsServer;

        private void Awake()
        {
            m_LocalMaxAnger = SanitizeMax(defaultMaxAnger);
            m_LocalAnger = 0f;
            m_CurrentStage = CalculateStage(0f, m_LocalMaxAnger);
            m_StageInitialized = true;
        }

        public override void OnNetworkSpawn()
        {
            m_NetworkAnger.OnValueChanged += HandleNetworkAngerChanged;
            m_NetworkMaxAnger.OnValueChanged += HandleNetworkMaxChanged;

            if (IsServer)
            {
                m_NetworkMaxAnger.Value = SanitizeMax(m_LocalMaxAnger);
                m_NetworkAnger.Value = Sanitize(m_LocalAnger);
            }

            m_CurrentStage = CalculateStage(CurrentAnger, MaxAnger);
            m_StageInitialized = true;
            NotifyAngerChanged();
        }

        public override void OnNetworkDespawn()
        {
            m_NetworkAnger.OnValueChanged -= HandleNetworkAngerChanged;
            m_NetworkMaxAnger.OnValueChanged -= HandleNetworkMaxChanged;
        }

        public void AddAnger(float amount)
        {
            if (!HasAuthority || !IsFinite(amount)) return;
            SetAnger(CurrentAnger + amount);
        }

        public void RemoveAnger(float amount)
        {
            if (!HasAuthority || !IsFinite(amount)) return;
            SetAnger(CurrentAnger - amount);
        }

        public void SetAnger(float value)
        {
            if (!HasAuthority) return;
            float next = Mathf.Clamp(Sanitize(value), 0f, MaxAnger);

            if (IsNetworkStateActive)
            {
                if (!Mathf.Approximately(m_NetworkAnger.Value, next))
                    m_NetworkAnger.Value = next;
                return;
            }

            if (Mathf.Approximately(m_LocalAnger, next)) return;
            m_LocalAnger = next;
            NotifyAngerChanged();
        }

        public void ResetAnger()
        {
            SetAnger(0f);
        }

        public float GetAnger() => CurrentAnger;

        public float GetNormalizedAnger()
        {
            float max = MaxAnger;
            return max <= 0f ? 0f : Mathf.Clamp01(CurrentAnger / max);
        }

        public AngerStage GetCurrentStage() => CalculateStage(CurrentAnger, MaxAnger);

        public void SetMaxAnger(float value)
        {
            if (!HasAuthority) return;
            float nextMax = SanitizeMax(value);

            if (IsNetworkStateActive)
            {
                m_NetworkMaxAnger.Value = nextMax;
                m_NetworkAnger.Value = Mathf.Clamp(Sanitize(m_NetworkAnger.Value), 0f, nextMax);
                return;
            }

            m_LocalMaxAnger = nextMax;
            m_LocalAnger = Mathf.Clamp(Sanitize(m_LocalAnger), 0f, nextMax);
            NotifyAngerChanged();
        }

        private void HandleNetworkAngerChanged(float previous, float current)
        {
            NotifyAngerChanged();
        }

        private void HandleNetworkMaxChanged(float previous, float current)
        {
            if (IsServer)
                m_NetworkAnger.Value = Mathf.Clamp(Sanitize(m_NetworkAnger.Value), 0f, MaxAnger);
            NotifyAngerChanged();
        }

        private void NotifyAngerChanged()
        {
            float anger = Mathf.Clamp(Sanitize(CurrentAnger), 0f, MaxAnger);
            float max = SanitizeMax(MaxAnger);
            float normalized = max <= 0f ? 0f : Mathf.Clamp01(anger / max);
            AngerStage nextStage = CalculateStage(anger, max);
            AngerStage previousStage = m_CurrentStage;

            m_CurrentStage = nextStage;
            OnAngerChanged?.Invoke(anger, max, normalized);
            if (m_StageInitialized && previousStage != nextStage)
                OnAngerStageChanged?.Invoke(previousStage, nextStage);
            m_StageInitialized = true;
        }

        private static AngerStage CalculateStage(float anger, float max)
        {
            float normalized = max <= 0f ? 0f : Mathf.Clamp01(Sanitize(anger) / SanitizeMax(max));
            if (normalized <= 0.30f) return AngerStage.Calm;
            if (normalized <= 0.60f) return AngerStage.Annoyed;
            if (normalized <= 0.90f) return AngerStage.Angry;
            return AngerStage.Furious;
        }

        private static float Sanitize(float value)
        {
            return IsFinite(value) ? Mathf.Max(0f, value) : 0f;
        }

        private static float SanitizeMax(float value)
        {
            return IsFinite(value) ? Mathf.Max(0.0001f, value) : 100f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
