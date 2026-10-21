using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GameNet.Gameplay.EditorTools
{
    /// <summary>
    /// 所有别墅区域（地板/障碍物/墙/门）的 Scene 视图编辑器。
    ///
    /// ============================ 两套线框分别在哪儿 ============================
    /// 【实际位置线框（黄色）】→ Scene 3D 视图里地面上的真实四边形（碰撞体所在），
    ///     4 个黄点可以在 3D 里拖（鼠标射线落在地面上）。
    /// 【投影位置线框（绿色）】→ 就是“它在游戏画面里盖住图上的哪一块”，所以画在
    ///     ① Game 视图（运行时/编辑时都画，盖在别墅图上）
    ///     ② Scene 视图右上角的【图纸对齐面板】里，和图纸画在同一张 2D 面板上。
    ///
    /// ============================ 为什么用 2D 面板 ============================
    /// 图纸是 2D 画面，而 3D 场景里“肉眼看着对齐”必然受视差影响：
    /// 地面（y≈0）和图纸（屏幕平面）深度差一大截，任何角度看“对上了”都是错觉，
    /// 一旦投影回游戏画面就偏。面板里图纸和绿色框**共面且在 2D 屏幕上**，
    /// 拖哪个点就是哪个点，跟相机角度完全无关。
    ///
    /// 用法：选中某个区域物体 → 在右上角面板里拖 4 个绿点，贴住图纸里房间地板/柜子的 4 个角。
    /// </summary>
    [CustomEditor(typeof(VillaZoneBase), true)]
    public class VillaZoneSceneEditor : Editor
    {
        private static readonly string[] CornerLabels = { "0 左上", "1 右上", "2 右下", "3 左下" };

        private const float DotSize = 11f;
        private const float HitPadding = 6f;

        private static bool ShowPanel
        {
            get { return EditorPrefs.GetBool("VillaShowAlignPanel", true); }
            set { EditorPrefs.SetBool("VillaShowAlignPanel", value); }
        }

        private static float PanelWidth
        {
            get { return EditorPrefs.GetFloat("VillaAlignPanelWidth", 460f); }
            set { EditorPrefs.SetFloat("VillaAlignPanelWidth", value); }
        }

        private int _dragIndex = -1;

        // ------------------------------------------------------------------
        // Inspector
        // ------------------------------------------------------------------
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            // 门：targetDoor 一变就自动双向配对（A↔B 互指互传，改/清/抢自动维护）
            var door = target as VillaDoor;
            if (door != null)
            {
                SyncDoorPair(door);
                DrawDoorPairStatus(door);
            }

            GUILayout.Space(8);
            GUILayout.Label("摆放辅助", EditorStyles.boldLabel);

            if (GUILayout.Button(ShowPanel ? "隐藏图纸对齐面板（Scene 视图右上角）" : "显示图纸对齐面板（Scene 视图右上角）"))
            {
                ShowPanel = !ShowPanel;
                SceneView.RepaintAll();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("面板变小"))
            {
                PanelWidth = Mathf.Max(240f, PanelWidth - 80f);
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("面板变大"))
            {
                PanelWidth = Mathf.Min(900f, PanelWidth + 80f);
                SceneView.RepaintAll();
            }

            GUILayout.EndHorizontal();

            if (GUILayout.Button("对齐视角到游戏相机（机位 + 视场角同步）"))
            {
                AlignSceneViewToGameCamera();
            }

            EditorGUILayout.HelpBox(
                "摆放方式（推荐）：在右上角【图纸对齐面板】里拖 4 个绿点，\n" +
                "让绿色四边形贴住图纸里该房间地板的 4 个角。面板是 2D 的，\n" +
                "和 Scene 视角、相机角度完全无关，对上就是准的。\n\n" +
                "黄色四边形/黄点 = 3D 里的实际位置（自动跟着绿点走，不用管它歪不歪）。\n" +
                "Game 视图里也会实时显示绿色投影线框，可以两边对照。",
                MessageType.Info);
        }

        private static void AlignSceneViewToGameCamera()
        {
            Camera cam = Camera.main;
            SceneView sv = SceneView.lastActiveSceneView;
            if (cam == null || sv == null)
            {
                Debug.LogWarning("找不到 Main Camera（确认相机 Tag 是 MainCamera）。");
                return;
            }

            sv.AlignViewToObject(cam.transform);
            sv.orthographic = false;

            SceneView.CameraSettings cs = sv.cameraSettings;
            cs.fieldOfView = cam.fieldOfView;
            cs.dynamicClip = false;
            sv.cameraSettings = cs;

            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------
        // 门配对：两扇门为一组，互相传送
        // ------------------------------------------------------------------

        /// <summary>
        /// 监听 Inspector 里 targetDoor 的变化并自动双向同步：
        ///   * 指向新门 B → B 自动指回本门（B 若原本和 C 配对，C 自动解绑）；
        ///   * 清空 → 旧搭档的回指自动解除；
        ///   * 指向自己 → 视为清空。
        /// 每扇门用 pairedDoor 记录“上次配的那扇”，据此判断发生了什么变化。
        /// </summary>
        private static void SyncDoorPair(VillaDoor door)
        {
            VillaDoor target = door.targetDoor;
            VillaDoor last = door.pairedDoor;

            if (target == door)
            {
                // 不允许自环：当成清空处理
                Undo.RecordObject(door, "配对门");
                door.targetDoor = null;
                target = null;
            }

            if (target == last && (target == null || target.targetDoor == door))
            {
                return; // 没有变化，或对方回指已就位（比如对方那边发起的配对）
            }

            Undo.RecordObject(door, "配对门");

            // 1) 旧搭档的回指解除（双向解绑：任何一边清空/改指向，另一边自动断开）
            if (last != null && last != target && last.targetDoor == door)
            {
                Undo.RecordObject(last, "配对门");
                last.targetDoor = null;
                last.pairedDoor = null;
                EditorUtility.SetDirty(last);
            }

            // 2) 新搭档回指本门（把它从原来的搭档手里接过来）
            if (target != null)
            {
                VillaDoor oldPartner = target.targetDoor;
                if (oldPartner != null && oldPartner != door)
                {
                    Undo.RecordObject(oldPartner, "配对门");
                    oldPartner.targetDoor = null;
                    oldPartner.pairedDoor = null;
                    EditorUtility.SetDirty(oldPartner);
                }

                Undo.RecordObject(target, "配对门");
                target.targetDoor = door;
                target.pairedDoor = door;
                EditorUtility.SetDirty(target);
            }

            door.pairedDoor = target;
            EditorUtility.SetDirty(door);
        }

        /// <summary>Inspector 上显示配对状态。</summary>
        private static void DrawDoorPairStatus(VillaDoor door)
        {
            GUILayout.Space(4);
            if (door.IsPaired)
            {
                EditorGUILayout.HelpBox(
                    string.Format("已配对：门[{0}] ↔ 门[{1}]，两边互相传送。\n走进任一扇 → 传送到另一扇的落点。",
                        string.IsNullOrEmpty(door.doorLabel) ? door.name : door.doorLabel,
                        string.IsNullOrEmpty(door.targetDoor.doorLabel) ? door.targetDoor.name : door.targetDoor.doorLabel),
                    MessageType.None);
            }
            else if (door.targetDoor != null)
            {
                EditorGUILayout.HelpBox("对方还没回指（一般会立即自动完成，若一直停留请重新拖一次目标门）。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("未配对：把另一扇门拖进「配对门 targetDoor」即可自动成组（互相传送）。", MessageType.Info);
            }
        }

        // ------------------------------------------------------------------
        // Scene 视图
        // ------------------------------------------------------------------
        public void OnSceneGUI()
        {
            var zone = (VillaZoneBase)target;
            VillaRoomBuilder builder = zone.Builder;
            if (builder == null)
            {
                return;
            }

            // 先处理 2D 面板（它画在最上层，事件也优先），再处理 3D 手柄
            if (ShowPanel)
            {
                DrawAlignPanel(zone, builder);
            }

            Vector3[] world = zone.GetWorldCorners();
            if (world.Length != 4)
            {
                return;
            }

            // ---------------- 黄色实际位置线框（3D 地面上的真实四边形） ----------------
            Handles.color = zone.actualWireColor;
            for (int k = 0; k < 4; k++)
            {
                Handles.DrawLine(world[k], world[(k + 1) % 4], 3f);
            }

            Vector3 center = (world[0] + world[1] + world[2] + world[3]) * 0.25f;
            Handles.Label(center + Vector3.up * 0.05f,
                zone.GetType().Name + "  " + zone.name + "（实际位置·由绿点自动算出）",
                MakeStyle(zone.actualWireColor));

            // ---------------- 黄色顶点：在地面上拖（备用） ----------------
            Vector3 groundOrigin = new Vector3(0f, builder.groundHeight, 0f);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                Vector3 pos = world[i];
                float size = HandleUtility.GetHandleSize(pos) * 0.075f;

                DragVertexHandle(pos, Quaternion.identity, size, Handles.CircleHandleCap,
                    zone.actualWireColor,
                    ray => RayPoint(ray, groundOrigin, Vector3.up),
                    () => Undo.RecordObject(zone, "拖拽别墅区域顶点（地面）"),
                    p =>
                    {
                        // 夹一下，防止鼠标射线掠射地面时算出天文数字坐标
                        zone.corners[index] = VillaZoneBase.SanitizeNorm(builder.GroundToImageNorm(p));
                        EditorUtility.SetDirty(zone);
                        InternalEditorUtility.RepaintAllViews();
                    });

                Handles.Label(pos + Vector3.up * size * 1.4f, CornerLabels[i], MakeStyle(zone.actualWireColor));
            }
        }

        // ==================================================================
        // 图纸对齐面板：Scene 视图右上角的一块 2D 面板，
        // 里面画“游戏画面 + 别墅图 + 当前区域的绿色四边形 + 4 个可拖拽绿点”。
        // 拖点是纯 2D 像素映射 → 不存在视差，跟相机角度无关。
        // ==================================================================
        private void DrawAlignPanel(VillaZoneBase zone, VillaRoomBuilder builder)
        {
            SceneView sv = SceneView.currentDrawingSceneView;
            if (sv == null)
            {
                return;
            }

            Texture tex = null;
            if (builder.villaImage != null && builder.villaImage.sprite != null)
            {
                tex = builder.villaImage.sprite.texture;
            }

            Handles.BeginGUI();
            try
            {
                float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
                if (aspect < 0.05f || aspect > 20f)
                {
                    aspect = 16f / 9f;
                }

                const float pad = 8f;
                const float header = 22f;
                const float footer = 22f;
                float top = 34f; // 让开 Scene 视图工具栏
                float availH = Mathf.Max(140f, sv.position.height - top - 46f);

                float pw = Mathf.Min(PanelWidth, sv.position.width - 48f, availH * aspect);
                pw = Mathf.Max(200f, pw);
                float ph = pw / aspect;

                var panel = new Rect(sv.position.width - pw - pad * 2f - 12f, top,
                                     pw + pad * 2f, ph + header + footer + pad * 2f);
                if (panel.x < 4f)
                {
                    panel.x = 4f;
                }

                var gameRect = new Rect(panel.x + pad, panel.y + header + pad, pw, ph);

                // 底板
                EditorGUI.DrawRect(panel, new Color(0.12f, 0.12f, 0.14f, 0.95f));
                EditorGUI.DrawRect(gameRect, new Color(0.05f, 0.05f, 0.06f, 1f));
                GUI.Label(new Rect(panel.x + pad, panel.y + 3f, panel.width, 18f),
                    "图纸对齐面板：拖 4 个绿点贴住房间地板", EditorStyles.boldLabel);

                // 图纸在游戏画面里的归一化范围（一般是整屏 0~1）
                float u0, u1, v0, v1;
                GetImageScreenRange(builder, out u0, out u1, out v0, out v1);

                // 画图纸
                if (tex != null)
                {
                    var imgRect = new Rect(gameRect.x + u0 * gameRect.width,
                                           gameRect.y + v0 * gameRect.height,
                                           Mathf.Max(1f, (u1 - u0) * gameRect.width),
                                           Mathf.Max(1f, (v1 - v0) * gameRect.height));
                    GUI.DrawTexture(imgRect, tex, ScaleMode.StretchToFill, false);
                }

                // 区域 4 个角 → 面板坐标
                var pts = new Vector2[4];
                for (int i = 0; i < 4; i++)
                {
                    pts[i] = NormToPanel(zone.corners[i], gameRect, u0, u1, v0, v1);
                }

                // ---------------- 事件：面板内的拖拽（纯 2D 映射） ----------------
                Event e = Event.current;
                int id = GUIUtility.GetControlID(FocusType.Passive);

                if (e.type == EventType.MouseDown && e.button == 0 && GUIUtility.hotControl == 0)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var hit = new Rect(pts[i].x - DotSize - HitPadding, pts[i].y - DotSize - HitPadding,
                                           (DotSize + HitPadding) * 2f, (DotSize + HitPadding) * 2f);
                        if (hit.Contains(e.mousePosition))
                        {
                            _dragIndex = i;
                            GUIUtility.hotControl = id;
                            Undo.RecordObject(zone, "拖拽别墅区域顶点（面板）");
                            e.Use();
                            break;
                        }
                    }
                }
                else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && _dragIndex >= 0)
                {
                    zone.corners[_dragIndex] = PanelToNorm(e.mousePosition, gameRect, u0, u1, v0, v1);
                    EditorUtility.SetDirty(zone);
                    InternalEditorUtility.RepaintAllViews();
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    _dragIndex = -1;
                    e.Use();
                }

                // ---------------- 画绿色四边形（投影位置） ----------------
                Handles.color = zone.projectedWireColor;
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    Handles.DrawAAPolyLine(3f,
                        new Vector3(pts[i].x, pts[i].y, 0f),
                        new Vector3(pts[j].x, pts[j].y, 0f));
                }

                // ---------------- 画 4 个绿点 + 编号 ----------------
                for (int i = 0; i < 4; i++)
                {
                    Color c = _dragIndex == i ? Color.white : zone.projectedWireColor;
                    var dot = new Rect(pts[i].x - DotSize * 0.5f, pts[i].y - DotSize * 0.5f, DotSize, DotSize);
                    EditorGUI.DrawRect(dot, c);
                    GUI.Label(new Rect(pts[i].x + DotSize, pts[i].y - 9f, 60f, 18f),
                        CornerLabels[i], MakeStyle(c));
                }

                // ---------------- 底部信息 ----------------
                GUI.Label(new Rect(panel.x + pad, panel.yMax - footer - 2f, panel.width - 20f, 18f),
                    zone.name + "   (绿线 = 它在游戏画面里盖住的位置)", EditorStyles.miniLabel);

                // 面板尺寸
                if (GUI.Button(new Rect(panel.xMax - pad - 46f, panel.yMax - footer - 2f, 20f, 16f), "－", EditorStyles.miniButtonLeft))
                {
                    PanelWidth = Mathf.Max(240f, PanelWidth - 80f);
                    SceneView.RepaintAll();
                }

                if (GUI.Button(new Rect(panel.xMax - pad - 26f, panel.yMax - footer - 2f, 20f, 16f), "＋", EditorStyles.miniButtonRight))
                {
                    PanelWidth = Mathf.Min(900f, PanelWidth + 80f);
                    SceneView.RepaintAll();
                }
            }
            finally
            {
                Handles.EndGUI();
            }
        }

        /// <summary>图纸在游戏画面里的归一化范围（左上为 0,0，右下为 1,1）。异常时回退整屏。</summary>
        private static void GetImageScreenRange(VillaRoomBuilder builder, out float u0, out float u1, out float v0, out float v1)
        {
            u0 = 0f;
            u1 = 1f;
            v0 = 0f;
            v1 = 1f;

            if (builder.villaImage == null)
            {
                return;
            }

            float sw = Mathf.Max(1f, Screen.width);
            float sh = Mathf.Max(1f, Screen.height);

            Vector2 tl = builder.ImageNormToScreen(new Vector2(0f, 0f)); // 左下原点像素
            Vector2 br = builder.ImageNormToScreen(new Vector2(1f, 1f));

            float a0 = tl.x / sw;
            float a1 = br.x / sw;
            float b0 = 1f - tl.y / sh; // 转成“从上往下”
            float b1 = 1f - br.y / sh;

            // 明显异常（没布局好 / 换算失败）就当成整屏
            if (a0 < -0.05f || a1 > 1.05f || a1 - a0 < 0.05f ||
                b0 < -0.05f || b1 > 1.05f || b1 - b0 < 0.05f)
            {
                return;
            }

            u0 = Mathf.Clamp01(Mathf.Min(a0, a1));
            u1 = Mathf.Clamp01(Mathf.Max(a0, a1));
            v0 = Mathf.Clamp01(Mathf.Min(b0, b1));
            v1 = Mathf.Clamp01(Mathf.Max(b0, b1));
        }

        /// <summary>图坐标（0~1，左上原点）→ 面板坐标。</summary>
        private static Vector2 NormToPanel(Vector2 norm, Rect game, float u0, float u1, float v0, float v1)
        {
            float u = u0 + (u1 - u0) * Mathf.Clamp01(norm.x);
            float v = v0 + (v1 - v0) * Mathf.Clamp01(norm.y);
            return new Vector2(game.x + u * game.width, game.y + v * game.height);
        }

        /// <summary>面板坐标 → 图坐标（0~1，左上原点）。</summary>
        private static Vector2 PanelToNorm(Vector2 p, Rect game, float u0, float u1, float v0, float v1)
        {
            float u = Mathf.InverseLerp(game.x, game.xMax, p.x);
            float v = Mathf.InverseLerp(game.y, game.yMax, p.y);

            float x = Mathf.Approximately(u1, u0) ? 0.5f : (u - u0) / (u1 - u0);
            float y = Mathf.Approximately(v1, v0) ? 0.5f : (v - v0) / (v1 - v0);
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        }

        // ==================================================================
        // 3D 顶点手柄（黄色，贴地面拖动）
        // 内置 Handles.FreeMoveHandle 的控件 ID 依赖顶点位置，而顶点每帧都在动，
        // 会丢掉拖动控制权；这里用固定顺序的 ID 自己处理按下/拖动/抬起。
        // ==================================================================
        private static void DragVertexHandle(Vector3 position, Quaternion rotation, float size,
                                             Handles.CapFunction cap, Color color,
                                             System.Func<Ray, Vector3?> convert,
                                             System.Action onBeginDrag,
                                             System.Action<Vector3> onDrag)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.type)
            {
                case EventType.Layout:
                    HandleUtility.AddControl(id, HandleUtility.DistanceToCircle(position, size));
                    break;

                case EventType.MouseDown:
                    if (e.button == 0 && GUIUtility.hotControl == 0 &&
                        HandleUtility.nearestControl == id)
                    {
                        GUIUtility.hotControl = id;
                        if (onBeginDrag != null)
                        {
                            onBeginDrag();
                        }

                        e.Use();
                    }

                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        Vector3? p = convert(HandleUtility.GUIPointToWorldRay(e.mousePosition));
                        if (p.HasValue && onDrag != null)
                        {
                            onDrag(p.Value);
                        }

                        e.Use();
                    }

                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }

                    break;

                case EventType.Repaint:
                    bool hot = GUIUtility.hotControl == id;
                    using (new Handles.DrawingScope(hot ? Color.white : color))
                    {
                        cap(id, position, rotation, size, EventType.Repaint);
                    }

                    break;
            }
        }

        /// <summary>射线 ∩（过 planeOrigin、法线 planeNormal 的平面）。</summary>
        private static Vector3? RayPoint(Ray ray, Vector3 planeOrigin, Vector3 planeNormal)
        {
            float denom = Vector3.Dot(planeNormal, ray.direction);
            if (Mathf.Abs(denom) < 1e-6f)
            {
                return null;
            }

            float t = Vector3.Dot(planeNormal, planeOrigin - ray.origin) / denom;
            return ray.origin + ray.direction * t;
        }

        private static GUIStyle MakeStyle(Color c)
        {
            var s = new GUIStyle();
            s.normal.textColor = c;
            s.fontSize = 13;
            s.fontStyle = FontStyle.Bold;
            return s;
        }
    }
}
