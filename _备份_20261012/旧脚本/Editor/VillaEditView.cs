using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using GameNet.Gameplay;

namespace GameNet.EditorTools
{
    /// <summary>
    /// 一次编辑会话的上下文：把「别墅图」这张 UI（编辑模式=Canvas 下的别墅，Play=世界空间平面上的别墅）
    /// 当作坐标基准，用它的真实世界四角做换算，辅助线永远和图片严丝合缝。
    /// </summary>
    internal class VillaEditContext
    {
        public RectTransform image;   // 别墅 Image 的 RectTransform
        public Vector2 pixelSize;     // 图片像素尺寸
        public float unitsPerPixel;   // 1 像素 = 多少世界单位
        public float pitchRad;        // 相机俯角（弧度）

        /// <summary>图片四角世界坐标：0=左下 1=左上 2=右上 3=右下</summary>
        public readonly Vector3[] corners = new Vector3[4];

        public bool IsValid => image != null && pixelSize.x > 0f && unitsPerPixel > 0f;

        public float SinPitch => Mathf.Max(0.01f, Mathf.Sin(pitchRad));

        /// <summary>刷新四角世界坐标（相机/布局可能每帧都在变）</summary>
        public void RefreshCorners()
        {
            if (image != null)
            {
                image.GetWorldCorners(corners);
            }
        }

        /// <summary>图片归一化坐标(0~1) → 图平面上的世界点（双线性，双色辅助线的“彩色框”用它）</summary>
        public Vector3 NormToPlane(Vector2 n)
        {
            Vector3 bottom = Vector3.Lerp(corners[0], corners[3], n.x);
            Vector3 top = Vector3.Lerp(corners[1], corners[2], n.x);
            return Vector3.Lerp(bottom, top, n.y);
        }

        /// <summary>图平面上的世界点 → 图片归一化坐标（手柄拖拽用它）</summary>
        public Vector2 PlaneToNorm(Vector3 p)
        {
            if (image == null)
            {
                return new Vector2(0.5f, 0.5f);
            }

            Vector3 local = image.InverseTransformPoint(p);
            Rect r = image.rect;
            return new Vector2(
                r.width > 0.0001f ? (local.x - r.xMin) / r.width : 0.5f,
                r.height > 0.0001f ? (local.y - r.yMin) / r.height : 0.5f);
        }

        /// <summary>图上的归一化四边形 → 地面 y=0 上的实际行走范围（黄色框）</summary>
        public Vector2[] ToGroundQuad(Vector2[] c)
        {
            return VillaWalkAreas.ToGroundQuad(c, pixelSize, unitsPerPixel, SinPitch);
        }
    }

    /// <summary>
    /// 编辑模式的「直接在你 Canvas 下的别墅图上编辑」显示准备（纯编辑器设置，不造任何多余物体）：
    ///
    /// 选中「行走区域」或「Oscar_玩家」时自动：
    ///   1. 把 Canvas 配成 Screen Space - Camera + 绑定主相机 + 平面距离 = 游戏距离，
    ///      并把「别墅」按游戏世界尺寸（像素×比例）居中摆好 →
    ///      别墅图在 Scene 视图里的位置与大小与 Play 完全一致，辅助线就画在它上面；
    ///   2. 把 Scene 视图切到游戏视角（每次选中只切一次）。
    ///
    /// 【不动你的相机】本类只读取相机姿态参数（俯角/高度/平面距离），从不修改主相机的
    /// 位置、旋转、投影、视域，也不会自动往相机上挂组件；切视角只改编辑器 Scene 视图本身。
    ///
    /// Play 时这些设置全部会被 VillaLevelBootstrap 重新配置
    /// （Canvas→Overlay、别墅→世界空间平面并重新布局），所以对游戏运行零影响。
    /// </summary>
    internal static class VillaEditView
    {
        public const string VillaName = "别墅";

        private static readonly VillaEditContext s_Context = new VillaEditContext();
        private static Object s_AlignedFor; // 已对齐过视角的选中物体

        [InitializeOnLoadMethod]
        private static void Init()
        {
            Selection.selectionChanged += OnSelectionChanged;
        }

        private static void OnSelectionChanged()
        {
            s_AlignedFor = null;
        }

        // ------------------------------------------------------------------
        // 参数读取（只读，不写相机）
        // ------------------------------------------------------------------

        public static Image FindVillaImage()
        {
            var go = GameObject.Find(VillaName);
            return go != null ? go.GetComponent<Image>() : null;
        }

        /// <summary>读 VillaSceneCamera 的参数（没有该组件时用默认值 30°/12/20）</summary>
        public static void ReadCameraParams(out float pitchDegrees, out float cameraHeight, out float planeDistance)
        {
            pitchDegrees = 30f;
            cameraHeight = 12f;
            planeDistance = 20f;

            Camera cam = Camera.main;
            var vsc = cam != null ? cam.GetComponent<VillaSceneCamera>() : null;
            if (vsc == null)
            {
                return;
            }

            var so = new SerializedObject(vsc);
            var p = so.FindProperty("pitchDegrees");
            if (p != null)
            {
                pitchDegrees = p.floatValue;
            }

            p = so.FindProperty("planeDistance");
            if (p != null)
            {
                planeDistance = p.floatValue;
            }

            p = so.FindProperty("cameraHeight");
            if (p != null)
            {
                cameraHeight = p.floatValue;
            }
        }

        /// <summary>读 Bootstrap 上的目标世界宽度（没有则用默认 16.77）</summary>
        public static float ReadSceneWorldWidth()
        {
            var bootstrap = Object.FindObjectOfType<VillaLevelBootstrap>();
            if (bootstrap != null)
            {
                var so = new SerializedObject(bootstrap);
                var p = so.FindProperty("sceneWorldWidth");
                if (p != null)
                {
                    return p.floatValue;
                }
            }

            return 16.77f;
        }

        // ------------------------------------------------------------------
        // 编辑视图
        // ------------------------------------------------------------------

        /// <summary>
        /// 准备好编辑视图并返回上下文（失败返回 null）。
        /// selectionKey：当前选中的物体，用于“每次选中只对齐一次视角”。
        /// </summary>
        public static VillaEditContext EnsureEditView(Object selectionKey)
        {
            if (Application.isPlaying)
            {
                return null;
            }

            Image villa = FindVillaImage();
            if (villa == null || villa.sprite == null)
            {
                return null;
            }

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                return null;
            }

            ReadCameraParams(out float pitchDeg, out _, out float dist);
            float worldWidth = ReadSceneWorldWidth();

            Vector2 px = new Vector2(villa.sprite.rect.width, villa.sprite.rect.height);
            if (px.x <= 0f || px.y <= 0f)
            {
                return null;
            }

            // 让 Canvas 在编辑模式把你的别墅图渲染出来（与 Play 同位置同大小）
            ConfigureCanvasForEditing(villa, mainCam, dist, px, worldWidth);

            // 每次选中只把 Scene 视图切到游戏视角一次（之后你自由缩放不再被打断）
            if (s_AlignedFor != selectionKey)
            {
                s_AlignedFor = selectionKey;
                AlignSceneView();
                SceneView.RepaintAll();
            }

            // 上下文
            s_Context.image = villa.rectTransform;
            s_Context.pixelSize = px;
            s_Context.unitsPerPixel = worldWidth / px.x;
            s_Context.pitchRad = pitchDeg * Mathf.Deg2Rad;
            s_Context.RefreshCorners();
            return s_Context;
        }

        /// <summary>Play 模式：用运行时真实平面/相机组上下文（辅助线与游戏画面同一套数据）</summary>
        public static VillaEditContext FromRuntimePlane(VillaScenePlane plane, VillaSceneCamera sceneCamera)
        {
            if (plane == null || sceneCamera == null || plane.SceneImage == null)
            {
                return null;
            }

            s_Context.image = plane.SceneImage.rectTransform;
            s_Context.pixelSize = plane.ImagePixelSize;
            s_Context.unitsPerPixel = plane.UnitsPerPixel;
            s_Context.pitchRad = sceneCamera.PitchRadians;
            s_Context.RefreshCorners();
            return s_Context.IsValid ? s_Context : null;
        }

        /// <summary>
        /// 编辑模式把 Canvas 配成「Screen Space - Camera + 主相机」，
        /// 并让「别墅」按游戏世界尺寸居中铺好 —— 编辑时看到的画面与 Play 完全一致。
        /// Play 时 Bootstrap 会把 Canvas 改回 Overlay、别墅搬到世界空间平面重新布局，互不影响。
        /// 注意：这里只改 Canvas 与别墅 Image，不碰主相机。
        /// </summary>
        private static void ConfigureCanvasForEditing(Image villa, Camera mainCam, float planeDistance,
            Vector2 px, float worldWidth)
        {
            var canvas = villa.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != mainCam)
            {
                Undo.RecordObject(canvas, "编辑模式显示别墅图");
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = mainCam;
                EditorUtility.SetDirty(canvas);
            }

            if (!Mathf.Approximately(canvas.planeDistance, planeDistance))
            {
                Undo.RecordObject(canvas, "编辑模式显示别墅图");
                canvas.planeDistance = planeDistance;
                EditorUtility.SetDirty(canvas);
            }

            var canvasRt = canvas.transform as RectTransform;
            var rect = villa.rectTransform;

            // 别墅居中
            if (rect.anchorMin != new Vector2(0.5f, 0.5f) || rect.anchorMax != new Vector2(0.5f, 0.5f)
                || rect.pivot != new Vector2(0.5f, 0.5f) || rect.anchoredPosition != Vector2.zero
                || rect.localScale != Vector3.one)
            {
                Undo.RecordObject(rect, "编辑模式显示别墅图");
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
                EditorUtility.SetDirty(rect);
            }

            // 按“画布单位↔世界单位”的实际比例，把别墅摆成游戏里的世界尺寸（像素×比例）
            if (canvasRt != null && canvasRt.rect.width > 0.0001f && canvasRt.rect.height > 0.0001f)
            {
                var cc = new Vector3[4];
                canvasRt.GetWorldCorners(cc);
                float canvasWorldW = Vector3.Distance(cc[0], cc[3]);
                float canvasWorldH = Vector3.Distance(cc[0], cc[1]);
                if (canvasWorldW > 0.0001f && canvasWorldH > 0.0001f)
                {
                    float s = worldWidth / px.x;
                    var target = new Vector2(
                        px.x * s * canvasRt.rect.width / canvasWorldW,
                        px.y * s * canvasRt.rect.height / canvasWorldH);
                    if ((rect.sizeDelta - target).sqrMagnitude > 0.01f)
                    {
                        Undo.RecordObject(rect, "编辑模式显示别墅图");
                        rect.sizeDelta = target;
                        EditorUtility.SetDirty(rect);
                    }
                }
            }

            if (villa.preserveAspect)
            {
                Undo.RecordObject(villa, "编辑模式显示别墅图");
                villa.preserveAspect = false;
                EditorUtility.SetDirty(villa);
            }

            // 别墅排到最后：编辑时盖住 HUD，正对地板不被界面元素挡住（Play 时别墅已移出该 Canvas）
            if (rect.parent != null && rect.GetSiblingIndex() != rect.parent.childCount - 1)
            {
                rect.SetAsLastSibling();
                EditorUtility.SetDirty(rect);
            }
        }

        // ------------------------------------------------------------------
        // Scene 视图对齐
        // ------------------------------------------------------------------

        /// <summary>把 Scene 视图切到与游戏完全相同的视角（正对别墅图，图铺满视图）</summary>
        public static void AlignSceneViewToGameView()
        {
            AlignSceneView();
        }

        private static void AlignSceneView()
        {
            Camera mainCam = Camera.main;
            SceneView sv = SceneView.lastActiveSceneView;
            Image villa = FindVillaImage();
            if (mainCam == null || sv == null || villa == null || villa.sprite == null)
            {
                return;
            }

            ReadCameraParams(out _, out _, out float dist);
            float worldWidth = ReadSceneWorldWidth();
            Vector2 px = new Vector2(villa.sprite.rect.width, villa.sprite.rect.height);
            float s = worldWidth / px.x;

            sv.orthographic = true;
            sv.pivot = mainCam.transform.position + mainCam.transform.forward * dist;
            sv.rotation = mainCam.transform.rotation;
            sv.size = px.y * s * 0.5f * 1.01f; // 半个视高
            sv.Repaint();
        }

        // ------------------------------------------------------------------
        // 区域轮廓绘制（编辑模式与 Play 模式共用）
        // ------------------------------------------------------------------

        /// <summary>
        /// 画每块行走区域的两条辅助线：
        /// 彩色=贴着别墅图的四边形（直接拖它的四角贴地板）；黄色=映射到 y=0 地面的实际范围。非交互。
        /// </summary>
        public static void DrawAreaOutlines(VillaWalkAreas wa, VillaEditContext ctx)
        {
            if (wa == null || ctx == null || !ctx.IsValid)
            {
                return;
            }

            ctx.RefreshCorners();
            var areas = wa.Areas;

            for (int i = 0; i < areas.Count; i++)
            {
                WalkArea area = areas[i];
                if (area == null)
                {
                    continue;
                }

                area.EnsureCorners();
                Vector2[] c = area.corners;

                var proj = new Vector3[4];
                for (int k = 0; k < 4; k++)
                {
                    proj[k] = ctx.NormToPlane(c[k]);
                }

                Color col = Color.HSVToRGB((i * 0.37f) % 1f, 0.9f, 1f);
                Handles.DrawSolidRectangleWithOutline(proj, new Color(col.r, col.g, col.b, 0.12f), col);
                Handles.Label((proj[0] + proj[1] + proj[2] + proj[3]) * 0.25f + Vector3.up * 0.3f,
                    area.name, EditorStyles.boldLabel);

                // 黄色：地面实际范围（y=0）
                Vector2[] ground = ctx.ToGroundQuad(c);
                Handles.color = Color.yellow;
                for (int k = 0; k < 4; k++)
                {
                    Handles.DrawLine(
                        new Vector3(ground[k].x, 0f, ground[k].y),
                        new Vector3(ground[(k + 1) % 4].x, 0f, ground[(k + 1) % 4].y));
                }

                Handles.color = Color.white;
            }
        }
    }
}
