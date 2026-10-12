using UnityEditor;
using UnityEngine;
using GameNet.Gameplay;

namespace GameNet.EditorTools
{
    /// <summary>
    /// 「行走区域」的 Scene 视图辅助线 + 拖拽编辑器（只在编辑器里出现，不进游戏画面）：
    ///
    ///  - 编辑模式：选中本物体后，Scene 视图自动对到主相机视角，
    ///    并让你 Canvas 下那张「别墅」图直接显示出来（见 VillaEditView），辅助线就画在它上面：
    ///      · 彩色框 —— 贴着别墅图的区域（拖中心点整块移动、拖四个角自由变形，可贴住透视梯形地板）；
    ///      · 黄色框 —— 映射到 y=0 地面上的实际可行走范围；
    ///    换算用的是别墅图的真实世界四角，所以不管视图比例如何都对得齐。
    ///
    ///  - Play 模式：Bootstrap 运行时接线后，直接用真实平面/相机画辅助线。
    /// </summary>
    [CustomEditor(typeof(VillaWalkAreas))]
    public class VillaWalkAreasEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            var wa = (VillaWalkAreas)target;
            wa.EnsureCornersAll();

            // Play 模式用运行时真实平面；编辑模式用 Canvas 下你自己的别墅图
            VillaEditContext ctx = wa.PlaneRef != null && wa.CameraRef != null
                ? VillaEditView.FromRuntimePlane(wa.PlaneRef, wa.CameraRef)
                : VillaEditView.EnsureEditView(target);

            if (ctx == null || !ctx.IsValid)
            {
                if (!Application.isPlaying)
                {
                    Handles.Label(wa.transform.position,
                        "场景里找不到「别墅」Image 或 Main Camera，无法显示行走区域辅助线");
                }

                return;
            }

            DrawHandles(wa, ctx);
        }

        /// <summary>画区域轮廓 + 中心/四角拖拽手柄</summary>
        private void DrawHandles(VillaWalkAreas wa, VillaEditContext ctx)
        {
            ctx.RefreshCorners();

            // 区域轮廓（彩色=贴着别墅图、黄色=地面实际范围）
            VillaEditView.DrawAreaOutlines(wa, ctx);

            var areas = wa.Areas;
            const float handleSize = 0.25f;

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

                Vector3 centerWorld = (proj[0] + proj[1] + proj[2] + proj[3]) * 0.25f;

                // ---- 编辑：任何改动都先记录 Undo，改完标脏保存 ----
                Undo.RecordObject(wa, "调整行走区域");

                // 中心手柄：整块平移（保持形状）
                EditorGUI.BeginChangeCheck();
                Vector3 newCenter = Handles.FreeMoveHandle(centerWorld, handleSize, Vector3.zero, Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Vector2 d = ctx.PlaneToNorm(newCenter) - ctx.PlaneToNorm(centerWorld);

                    float minX = float.MaxValue, maxX = float.MinValue;
                    float minY = float.MaxValue, maxY = float.MinValue;
                    for (int k = 0; k < 4; k++)
                    {
                        minX = Mathf.Min(minX, c[k].x);
                        maxX = Mathf.Max(maxX, c[k].x);
                        minY = Mathf.Min(minY, c[k].y);
                        maxY = Mathf.Max(maxY, c[k].y);
                    }

                    d.x = Mathf.Clamp(d.x, -minX, 1f - maxX);
                    d.y = Mathf.Clamp(d.y, -minY, 1f - maxY);
                    for (int k = 0; k < 4; k++)
                    {
                        c[k] = new Vector2(Mathf.Clamp01(c[k].x + d.x), Mathf.Clamp01(c[k].y + d.y));
                    }
                }

                // 四角手柄：任意拖拽（拖成梯形正好贴住透视地板）
                for (int k = 0; k < 4; k++)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.FreeMoveHandle(proj[k], handleSize * 0.8f, Vector3.zero, Handles.DotHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Vector2 n = ctx.PlaneToNorm(moved);
                        c[k] = new Vector2(Mathf.Clamp01(n.x), Mathf.Clamp01(n.y));
                    }
                }

                areas[i].corners = c;
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(wa);
            }
        }

        /// <summary>Inspector 底部：视角按钮 + 添加/删除区域</summary>
        public override void OnInspectorGUI()
        {
            var wa = (VillaWalkAreas)target;
            wa.EnsureCornersAll();

            DrawDefaultInspector();

            GUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "选中本物体后，Scene 视图会自动对到主相机视角并把别墅图框满，" +
                "并直接显示你 Canvas 下那张「别墅」图，辅助线就画在它上面：\n" +
                "· 彩色框=贴着别墅图的区域（拖中心点移动、拖四个角自由变形，可贴住透视梯形地板）\n" +
                "· 黄色框=映射到地面的实际行走范围\n" +
                "辅助线只在编辑器显示，Play 游戏画面不受影响。区域之间的空隙就是墙；" +
                "楼梯处让上下楼层的四边形重叠即可通行。\n" +
                "角色出生点：选中「Oscar_玩家」直接拖到地板区域内即可（Play 时从那里出发）。",
                MessageType.Info);

            if (GUILayout.Button("把 Scene 视图对到相机视角（正对别墅图）"))
            {
                VillaEditView.AlignSceneViewToGameView();
            }

            if (GUILayout.Button("＋ 添加一块区域（居中）"))
            {
                Undo.RecordObject(wa, "添加行走区域");
                wa.Areas.Add(new WalkArea
                {
                    name = $"区域{wa.Areas.Count + 1}",
                    corners = new[]
                    {
                        new Vector2(0.35f, 0.35f), new Vector2(0.65f, 0.35f),
                        new Vector2(0.65f, 0.65f), new Vector2(0.35f, 0.65f),
                    },
                    migrated = true,
                });
                EditorUtility.SetDirty(wa);
            }

            if (wa.Areas.Count > 0 && GUILayout.Button("－ 删除最后一块区域"))
            {
                Undo.RecordObject(wa, "删除行走区域");
                wa.Areas.RemoveAt(wa.Areas.Count - 1);
                EditorUtility.SetDirty(wa);
            }
        }
    }
}
