using UnityEditor;
using UnityEngine;

namespace GameNet.Gameplay.EditorTools
{
    /// <summary>
    /// Scene 视图取景复位工具。
    ///
    /// ============================ 解决什么问题 ============================
    /// 编辑态看着“整个场景空了”，但 Play 一切正常——原因是 Scene 视图相机被拉到了
    /// 几十万单位外（按 F 聚焦到某个跑飞的物体时最容易发生），场景内容缩成看不见的一点，
    /// 同时控制台刷 "Screen position out of view frustum ..."。
    ///
    /// ============================ 提供两件事 ============================
    /// 1. 菜单 Tools/心灵驾到/复位 Scene 视图（回到别墅地面）：一键把相机拉回别墅地面区域；
    /// 2. 自动兜底：编辑器启动/编译后，若发现相机距离异常（> 6 万），自动复位一次，
    ///    避免每次都要手动救场。
    /// </summary>
    public static class VillaSceneViewFocus
    {
        private const string MenuPath = "Tools/心灵驾到/复位 Scene 视图（回到别墅地面）";

        /// <summary>超过这个距离就认为相机“拉爆了”。正常取景一般在几百~几千。</summary>
        private const float MaxSaneSize = 60000f;

        [MenuItem(MenuPath, false, 200)]
        private static void ResetView()
        {
            SceneView sv = SceneView.lastActiveSceneView ?? SceneView.currentDrawingSceneView;
            if (sv == null)
            {
                Debug.LogWarning("[SceneView] 没有打开的 Scene 视图，无法复位。");
                return;
            }

            FrameVilla(sv);
            Debug.Log("[SceneView] 已复位到别墅地面区域。");
        }

        [InitializeOnLoadMethod]
        private static void AutoHeal()
        {
            // 等一帧再检查，保证 Scene 视图已经建好
            EditorApplication.delayCall += () =>
            {
                SceneView sv = SceneView.lastActiveSceneView;
                if (sv == null)
                {
                    return;
                }

                if (sv.size > MaxSaneSize || float.IsNaN(sv.size) || float.IsInfinity(sv.size))
                {
                    Debug.LogWarning("[SceneView] 检测到相机距离异常（" + sv.size.ToString("F0") + "），自动复位到别墅地面区域。");
                    FrameVilla(sv);
                }
            };
        }

        /// <summary>把 Scene 相机对准别墅地面区域（用房间管理器把图坐标换算成世界坐标）。</summary>
        private static void FrameVilla(SceneView sv)
        {
            VillaRoomBuilder b = Object.FindObjectOfType<VillaRoomBuilder>();

            Vector3 pivot = Vector3.zero;
            float size = 800f;

            if (b != null)
            {
                // 取图上下部“地板带”的中点与横向跨度作为取景依据
                Vector3 mid = b.ImageNormToGround(new Vector2(0.5f, 0.8f));
                Vector3 left = b.ImageNormToGround(new Vector2(0.15f, 0.8f));
                Vector3 right = b.ImageNormToGround(new Vector2(0.85f, 0.8f));

                float width = Vector3.Distance(left, right);
                if (width > 1f)
                {
                    pivot = mid;
                    size = width * 1.4f;
                }
            }
            else
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    pivot = cam.transform.position;
                    size = 300f;
                }
            }

            sv.in2DMode = false;
            sv.LookAt(pivot, Quaternion.Euler(45f, 0f, 0f), size); // 俯视 45°，距离按地板宽度算
            sv.Repaint();
        }
    }
}
