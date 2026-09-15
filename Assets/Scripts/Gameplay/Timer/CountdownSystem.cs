using System;
using Unity.Netcode;
using UnityEngine;

namespace GameNet.Gameplay
{
    /// <summary>倒计时真实状态。联机时由 Host 驱动，Client 只接收 NetworkVariable。</summary>
    public sealed class CountdownSystem : NetworkBehaviour
    {
        [SerializeField, Min(0f)] private float defaultDurationSeconds = 600f;

        private readonly NetworkVariable<float> m_NetworkDuration = new NetworkVariable<float>(
            600f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> m_NetworkRemaining = new NetworkVariable<float>(
            600f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> m_NetworkRunning = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float m_LocalDuration;
        private float m_LocalRemaining;
        private bool m_LocalRunning;
        private bool m_TimeUpRaised;
        private int m_LastNotifiedSecond = -1;

        public float RemainingSeconds => IsNetworkStateActive ? Sanitize(m_NetworkRemaining.Value) : m_LocalRemaining;
        public float DurationSeconds => IsNetworkStateActive ? Sanitize(m_NetworkDuration.Value) : m_LocalDuration;
        public bool IsRunning => IsNetworkStateActive ? m_NetworkRunning.Value : m_LocalRunning;
        public event Action<float> OnTimeChanged;
        public event Action OnTimeUp;

        private bool IsNetworkStateActive => IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool HasAuthority => !IsNetworkStateActive || IsServer;

        private void Awake()
        {
            m_LocalDuration = Sanitize(defaultDurationSeconds);
            m_LocalRemaining = m_LocalDuration;
        }

        public override void OnNetworkSpawn()
        {
            m_NetworkDuration.OnValueChanged += HandleNetworkTimeChanged;
            m_NetworkRemaining.OnValueChanged += HandleNetworkTimeChanged;
            m_NetworkRunning.OnValueChanged += HandleNetworkRunningChanged;
            if (IsServer)
            {
                m_NetworkDuration.Value = m_LocalDuration;
                m_NetworkRemaining.Value = m_LocalRemaining;
                m_NetworkRunning.Value = m_LocalRunning;
            }
            NotifyTime(true);
        }

        public override void OnNetworkDespawn()
        {
            m_NetworkDuration.OnValueChanged -= HandleNetworkTimeChanged;
            m_NetworkRemaining.OnValueChanged -= HandleNetworkTimeChanged;
            m_NetworkRunning.OnValueChanged -= HandleNetworkRunningChanged;
        }

        private void Update()
        {
            if (!HasAuthority || !IsRunning) return;
            float next = Mathf.Max(0f, RemainingSeconds - Time.deltaTime);
            if (IsNetworkStateActive)
            {
                if (!Mathf.Approximately(m_NetworkRemaining.Value, next)) m_NetworkRemaining.Value = next;
            }
            else
            {
                m_LocalRemaining = next;
                NotifyTime(false);
            }

            if (next <= 0f && !m_TimeUpRaised)
            {
                m_TimeUpRaised = true;
                if (IsNetworkStateActive) m_NetworkRunning.Value = false;
                else m_LocalRunning = false;
                OnTimeUp?.Invoke();
            }
        }

        public void SetDuration(float seconds)
        {
            if (!HasAuthority) return;
            float duration = Sanitize(seconds);
            m_TimeUpRaised = false;
            if (IsNetworkStateActive)
            {
                m_NetworkDuration.Value = duration;
                m_NetworkRemaining.Value = duration;
                m_NetworkRunning.Value = false;
            }
            else
            {
                m_LocalDuration = duration;
                m_LocalRemaining = duration;
                m_LocalRunning = false;
                NotifyTime(true);
            }
        }

        public void StartTimer()
        {
            if (!HasAuthority) return;
            if (RemainingSeconds <= 0f) ResetTimer();
            m_TimeUpRaised = false;
            if (IsNetworkStateActive) m_NetworkRunning.Value = true;
            else m_LocalRunning = true;
            NotifyTime(true);
        }

        public void PauseTimer()
        {
            if (!HasAuthority) return;
            if (IsNetworkStateActive) m_NetworkRunning.Value = false;
            else m_LocalRunning = false;
        }

        public void ResumeTimer() => StartTimer();

        public void ResetTimer()
        {
            if (!HasAuthority) return;
            m_TimeUpRaised = false;
            if (IsNetworkStateActive)
            {
                m_NetworkRemaining.Value = DurationSeconds;
                m_NetworkRunning.Value = false;
            }
            else
            {
                m_LocalRemaining = m_LocalDuration;
                m_LocalRunning = false;
                NotifyTime(true);
            }
        }

        private void HandleNetworkTimeChanged(float previous, float current) => NotifyTime(false);
        private void HandleNetworkRunningChanged(bool previous, bool current) => NotifyTime(false);

        private void NotifyTime(bool force)
        {
            int second = Mathf.CeilToInt(RemainingSeconds);
            if (!force && second == m_LastNotifiedSecond) return;
            m_LastNotifiedSecond = second;
            OnTimeChanged?.Invoke(RemainingSeconds);
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        }
    }
}
