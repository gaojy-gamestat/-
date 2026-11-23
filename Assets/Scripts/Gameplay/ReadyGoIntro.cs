using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 关卡开场 READY / GO 字幕：进入「第X关」场景时先把 Time.timeScale 冻结为 0，
    /// 显示 READY（约 1.2 秒）→ GO（约 0.8 秒）→ 淡出，字幕播完才恢复 1 正式开始游戏。
    /// 冻结期间倒计时、杜克巡逻、玩家移动都不会走（它们都按 Time.deltaTime 计时）。
    /// 自动运行，无需挂任何物体；重新挑战重载关卡时会再播一次。
    /// </summary>
    public sealed class ReadyGoIntro : MonoBehaviour
    {
        private static bool _running;

        private const float ReadySeconds = 1.2f;
        private const float GoSeconds = 0.8f;
        private const float FadeSeconds = 0.3f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            if (!IsLevelScene(scene.name)) return;
            if (_running || FindObjectOfType<ReadyGoIntro>() != null) return;

            new GameObject("[开场字幕]").AddComponent<ReadyGoIntro>();
        }

        private static bool IsLevelScene(string name)
        {
            return !string.IsNullOrEmpty(name) && name.StartsWith("第") && name.EndsWith("关");
        }

        private void Start()
        {
            StartCoroutine(PlayRoutine());
        }

        private IEnumerator PlayRoutine()
        {
            _running = true;
            Time.timeScale = 0f;

            // 字体：优先用场景里已有 TMP 文字的字体（第一关任务栏就是 TMP）
            TMP_FontAsset font = null;
            TextMeshProUGUI existing = FindObjectOfType<TextMeshProUGUI>(true);
            if (existing != null) font = existing.font;

            GameObject canvasGo = new GameObject("开场字幕Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2500;

            GameObject labelGo = new GameObject("字幕", typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(canvasGo.transform, false);
            TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.fontSize = 160f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            RectTransform rt = label.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // READY：白→金黄渐变；GO：橙红渐变（更有冲击力）
            yield return ShowLabel(label, "READY", new VertexGradient(
                Color.white, new Color(1f, 0.93f, 0.55f),
                new Color(1f, 0.93f, 0.55f), new Color(1f, 0.78f, 0.28f)), ReadySeconds);
            yield return ShowLabel(label, "GO!", new VertexGradient(
                new Color(1f, 0.88f, 0.25f), new Color(1f, 0.5f, 0.1f),
                new Color(1f, 0.98f, 0.55f), new Color(1f, 0.38f, 0.08f)), GoSeconds);

            Destroy(canvasGo);
            Time.timeScale = 1f;
            _running = false;
            Debug.Log("[开场字幕] READY/GO 播放完毕，正式开始游戏。");
        }

        /// <summary>
        /// 艺术字样式：粗体 + 四角渐变填色 + 深棕描边。
        /// </summary>
        private static void ApplyArtStyle(TextMeshProUGUI label, VertexGradient gradient)
        {
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = 12f;
            label.enableVertexGradient = true;
            label.colorGradient = gradient;
            label.color = Color.white;

            // TMP 内置描边属性（自动启用描边关键字），不会影响场景里其他用同款字体的文字
            label.outlineWidth = 0.28f;
            label.outlineColor = new Color32(56, 30, 8, 255);
        }

        /// <summary>弹跳缩放入场（1.6 倍→1 倍）→ 停留 → 淡出（全部用真实时间，不受 timeScale=0 影响）。</summary>
        private static IEnumerator ShowLabel(TextMeshProUGUI label, string text, VertexGradient gradient, float holdSeconds)
        {
            label.text = text;
            ApplyArtStyle(label, gradient);
            RectTransform rt = label.rectTransform;

            const float PopSeconds = 0.35f;
            float t = 0f;
            while (t < PopSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / PopSeconds);
                float ease = 1f - (1f - k) * (1f - k);   // ease-out，先大后小“砸”下来
                rt.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, ease);
                yield return null;
            }
            rt.localScale = Vector3.one;

            yield return new WaitForSecondsRealtime(holdSeconds);

            t = FadeSeconds;
            Color c = label.color;
            while (t > 0f)
            {
                t -= Time.unscaledDeltaTime;
                c.a = Mathf.Clamp01(t / FadeSeconds);
                label.color = c;
                yield return null;
            }
            label.text = string.Empty;
        }
    }
}
