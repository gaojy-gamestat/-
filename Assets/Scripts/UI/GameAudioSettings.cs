using System;
using UnityEngine;
using UnityEngine.Audio;

namespace GameNet.UI
{
    /// <summary>
    /// 全游戏统一的音量设置：主菜单、关卡内设置面板共用这一份数据。
    /// 在哪里调，另一个地方打开面板时看到的也是同一个值，并且立刻生效（不用重启游戏）。
    ///
    /// 数值存在 PlayerPrefs 的 Vol_Master / Vol_Music / Vol_SFX 里，
    /// 和旧版 MenuPanelsController 用的 key 完全一样，老存档不会丢。
    /// </summary>
    public static class GameAudioSettings
    {
        private const string KeyMaster = "Vol_Master";
        private const string KeyMusic = "Vol_Music";
        private const string KeySfx = "Vol_SFX";

        public static float Master { get; private set; }
        public static float Music { get; private set; }
        public static float Sfx { get; private set; }

        /// <summary>可选：以后接了 AudioMixer，填进来就会自动按 dB 生效。</summary>
        public static AudioMixer Mixer;
        public static string MusicParam = "MusicVol";
        public static string SfxParam = "SFXVol";

        /// <summary>任何一处改了音量都会触发，打开着的音量条据此同步。</summary>
        public static event Action Changed;

        private static bool s_Loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void EnsureLoaded()
        {
            if (s_Loaded) return;
            s_Loaded = true;

            Master = PlayerPrefs.GetFloat(KeyMaster, 1f);
            Music = PlayerPrefs.GetFloat(KeyMusic, 1f);
            Sfx = PlayerPrefs.GetFloat(KeySfx, 1f);
            Apply();
        }

        public static void SetMaster(float value)
        {
            EnsureLoaded();
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(value, Master)) return;
            Master = value;
            PlayerPrefs.SetFloat(KeyMaster, value);
            Apply();
            Changed?.Invoke();
        }

        public static void SetMusic(float value)
        {
            EnsureLoaded();
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(value, Music)) return;
            Music = value;
            PlayerPrefs.SetFloat(KeyMusic, value);
            Apply();
            Changed?.Invoke();
        }

        public static void SetSfx(float value)
        {
            EnsureLoaded();
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(value, Sfx)) return;
            Sfx = value;
            PlayerPrefs.SetFloat(KeySfx, value);
            Apply();
            Changed?.Invoke();
        }

        /// <summary>把当前数值应用到真正的声音上。</summary>
        public static void Apply()
        {
            AudioListener.volume = Master;

            if (Mixer == null) return;
            Mixer.SetFloat(MusicParam, ToDecibel(Music));
            Mixer.SetFloat(SfxParam, ToDecibel(Sfx));
        }

        private static float ToDecibel(float value)
        {
            return value <= 0.0001f ? -80f : Mathf.Log10(value) * 20f;
        }
    }
}
