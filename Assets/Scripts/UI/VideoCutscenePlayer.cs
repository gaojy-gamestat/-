using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace GameNet.UI
{
    /// <summary>
    /// 运行时全屏过场视频：黑底 Canvas + RawImage + VideoPlayer，播完自动加载下一场景。
    ///
    /// 视频文件放在 Assets/Resources/视频/ 下（mp4 导入后即为 VideoClip 资源），
    /// 用法：VideoCutscenePlayer.Play("视频/单人测试过场", "关卡选择");
    /// nextSceneName 传 null 表示只播视频，播完由调用方自己接管（比如弹结算面板）。
    /// </summary>
    public sealed class VideoCutscenePlayer : MonoBehaviour
    {
        private static VideoCutscenePlayer _instance;
        private bool _playing;

        public static void Play(string videoResource, string nextSceneName, System.Action onFinished = null)
        {
            if (_instance == null)
            {
                _instance = new GameObject("[过场视频播放器]").AddComponent<VideoCutscenePlayer>();
            }
            _instance.StartCutscene(videoResource, nextSceneName, onFinished);
        }

        private void StartCutscene(string videoResource, string nextSceneName, System.Action onFinished)
        {
            if (_playing) return;
            _playing = true;
            // 播放器必须跨场景常驻：LoadScene 会卸载旧场景，如果协程宿主被销毁，
            // "销毁视频黑幕"的代码就永远执行不到，屏幕会卡在视频最后一帧。
            DontDestroyOnLoad(gameObject);
            StartCoroutine(PlayRoutine(videoResource, nextSceneName, onFinished));
        }

        private IEnumerator PlayRoutine(string videoResource, string nextSceneName, System.Action onFinished)
        {
            VideoClip clip = Resources.Load<VideoClip>(videoResource);
            if (clip == null)
            {
                Debug.LogError($"[过场视频] 找不到视频资源 Assets/Resources/{videoResource}（mp4 还没导入或路径不对），直接跳过过场。");
                _playing = false;
                LoadNext(nextSceneName);
                onFinished?.Invoke();
                yield break;
            }

            // 视频自带音频：BGM 暂停，播完再恢复
            GameNet.Audio.GameBgmDirector bgm = FindObjectOfType<GameNet.Audio.GameBgmDirector>();
            if (bgm != null) bgm.SetMuted(true);

            // 全屏黑底 Canvas，盖住一切 UI（排序值拉到最高）
            GameObject canvasGo = new GameObject("过场视频Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3000;
            DontDestroyOnLoad(canvasGo);

            GameObject rawGo = new GameObject("视频画面", typeof(RawImage), typeof(VideoPlayer), typeof(UnityEngine.AudioSource));
            rawGo.transform.SetParent(canvasGo.transform, false);
            RawImage raw = rawGo.GetComponent<RawImage>();
            RectTransform rawRt = raw.rectTransform;
            rawRt.anchorMin = new Vector2(0.5f, 0.5f);
            rawRt.anchorMax = new Vector2(0.5f, 0.5f);
            rawRt.pivot = new Vector2(0.5f, 0.5f);

            VideoPlayer player = rawGo.GetComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.renderMode = VideoRenderMode.APIOnly;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.SetTargetAudioSource(0, rawGo.GetComponent<UnityEngine.AudioSource>());
            player.isLooping = false;
            player.clip = clip;

            // 视频等比缩放到“完整可见”的最大尺寸，多余部分留黑边
            float videoAspect = clip.height > 1f ? (float)clip.width / clip.height : 16f / 9f;
            float screenAspect = (float)Screen.width / Screen.height;
            float w, h;
            if (videoAspect > screenAspect)
            {
                w = Screen.width;
                h = Screen.width / videoAspect;
            }
            else
            {
                h = Screen.height;
                w = Screen.height * videoAspect;
            }
            rawRt.sizeDelta = new Vector2(w, h);

            player.Prepare();
            while (!player.isPrepared) yield return null;
            if (player.audioTrackCount > 0) player.EnableAudioTrack(0, true);
            raw.texture = player.texture;
            player.Play();

            while (player.isPlaying)
            {
                raw.texture = player.texture;
                yield return null;
            }
            raw.texture = player.texture; // 补上最后一帧

            // 先切场景，等新场景真正激活后再撤掉视频黑幕，
            // 否则视频画面消失到场景切换完成之间会闪出旧界面
            bool sceneSwitching = !string.IsNullOrEmpty(nextSceneName)
                && Application.CanStreamedLevelBeLoaded(nextSceneName);
            string previousScene = SceneManager.GetActiveScene().name;
            LoadNext(nextSceneName);
            float deadline = Time.unscaledTime + 10f;
            while (sceneSwitching
                && SceneManager.GetActiveScene().name == previousScene
                && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            if (bgm != null) bgm.SetMuted(false);
            Destroy(canvasGo);
            _playing = false;
            onFinished?.Invoke();
        }

        private static void LoadNext(string nextSceneName)
        {
            if (string.IsNullOrEmpty(nextSceneName)) return;
            if (Application.CanStreamedLevelBeLoaded(nextSceneName))
            {
                SceneManager.LoadScene(nextSceneName);
            }
            else
            {
                Debug.LogError($"[过场视频] 场景 “{nextSceneName}” 没有加入 Build Settings，无法进入。");
            }
        }
    }
}
