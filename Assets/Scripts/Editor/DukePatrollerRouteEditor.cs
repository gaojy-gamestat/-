using System.Collections.Generic;
using GameNet.Gameplay;
using UnityEditor;
using UnityEngine;

namespace GameNet.EditorTools
{
    /// <summary>
    /// 杜克巡逻路线手绘工具。用法：
    /// 1. 选中 Duke 物体（挂 DukePatroller 的那个）；
    /// 2. Inspector 最底下「巡逻路线手绘工具」里选“正在编辑的站点”；
    /// 3. 勾选「手绘模式」，在 Scene 视图沿想让他走的路线依次点击地面，
    ///    每点一下往该站点 Route Points 末尾追加一个路点（橙色折线实时预览）；
    /// 4. 停靠点本身不用点——走完路点后会自动走到停靠位置；
    /// 5. 「撤销本站最后一个路点」「清空本站路点」随时改。
    /// 画完的路线存进 DukePatroller.stops[i].routePoints（序列化，直接生效）。
    /// </summary>
    [CustomEditor(typeof(DukePatroller))]
    public class DukePatrollerRouteEditor : UnityEditor.Editor
    {
        private static int _editStop;
        private static bool _drawMode;

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            DukePatroller duke = (DukePatroller)target;
            if (duke.stops == null || duke.stops.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("巡逻路线手绘工具", EditorStyles.boldLabel);

            string[] names = new string[duke.stops.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = $"{i}: {duke.stops[i].stopName}";
            }
            _editStop = Mathf.Clamp(_editStop, 0, duke.stops.Count - 1);
            _editStop = EditorGUILayout.Popup("正在编辑的站点", _editStop, names);

            _drawMode = EditorGUILayout.ToggleLeft(
                "手绘模式（Scene 视图点击地面 = 给该站点追加路点）", _drawMode);

            DukePatroller.PatrolStop stop = duke.stops[_editStop];
            EditorGUILayout.HelpBox(
                $"当前站点「{stop.stopName}」已有 {stop.routePoints.Count} 个路点。" +
                "手绘时只需点路过的地方（比如绕开家具的弯），走完路点后会自动走到停靠点。",
                MessageType.Info);

            EditorGUI.BeginDisabledGroup(stop.routePoints.Count == 0);
            if (GUILayout.Button("撤销本站最后一个路点"))
            {
                Undo.RecordObject(duke, "撤销巡逻路点");
                stop.routePoints.RemoveAt(stop.routePoints.Count - 1);
                EditorUtility.SetDirty(duke);
            }
            if (GUILayout.Button("清空本站路点"))
            {
                Undo.RecordObject(duke, "清空巡逻路点");
                stop.routePoints.Clear();
                EditorUtility.SetDirty(duke);
            }
            EditorGUI.EndDisabledGroup();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_drawMode) return;

            DukePatroller duke = (DukePatroller)target;
            VillaRoomBuilder room = duke.room != null ? duke.room : FindObjectOfType<VillaRoomBuilder>();
            if (room == null || !room.IsReady) return;
            if (duke.stops == null || duke.stops.Count == 0) return;

            int idx = Mathf.Clamp(_editStop, 0, duke.stops.Count - 1);
            DukePatroller.PatrolStop stop = duke.stops[idx];

            DrawRoutes(room, duke.stops, idx);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && !e.shift && !e.control)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                Plane plane = GroundPlane(room);
                if (plane.Raycast(ray, out float dist))
                {
                    Vector2 norm = room.GroundToImageNorm(ray.GetPoint(dist));
                    Undo.RecordObject(duke, "添加巡逻路点");
                    stop.routePoints.Add(norm);
                    EditorUtility.SetDirty(duke);
                    Debug.Log($"[巡逻手绘] 站点「{stop.stopName}」新增路点 ({norm.x:F3}, {norm.y:F3})");
                    e.Use();
                }
            }
        }

        /// <summary>地面平面：取第一块地板的世界高度（整个别墅地面等高）。</summary>
        private static Plane GroundPlane(VillaRoomBuilder room)
        {
            IList<VillaFloorArea> floors = room.Floors;
            for (int i = 0; i < floors.Count; i++)
            {
                if (floors[i] == null) continue;
                Vector3[] w = floors[i].GetWorldCorners();
                if (w.Length == 4)
                {
                    return new Plane(Vector3.up, w[0]);
                }
            }
            return new Plane(Vector3.up, 0f);
        }

        /// <summary>站点停靠位置（图坐标）：拖了 Floor Area 用区域中心+偏移，否则用 imageNorm。</summary>
        private static Vector2 StopNorm(DukePatroller.PatrolStop s)
        {
            if (s.floorArea != null)
            {
                return s.floorArea.CenterImageNorm + s.offset;
            }
            return s.imageNorm;
        }

        /// <summary>把每站路线画进 Scene 视图：正在编辑的站点实线，其他站点半透明。</summary>
        private static void DrawRoutes(VillaRoomBuilder room, List<DukePatroller.PatrolStop> stops, int editIdx)
        {
            for (int s = 0; s < stops.Count; s++)
            {
                bool active = s == editIdx;
                Handles.color = active
                    ? new Color(1f, 0.45f, 0.05f, 1f)
                    : new Color(1f, 0.45f, 0.05f, 0.25f);

                List<Vector3> pts = new List<Vector3>();
                for (int i = 0; i < stops[s].routePoints.Count; i++)
                {
                    Vector3 w = room.ImageNormToGround(stops[s].routePoints[i]);
                    pts.Add(w);
                    if (active)
                    {
                        Handles.DrawSolidDisc(w, Vector3.up, 0.02f);
                    }
                }
                Vector3 stopWorld = room.ImageNormToGround(StopNorm(stops[s]));
                pts.Add(stopWorld);

                if (pts.Count >= 2)
                {
                    Handles.DrawPolyLine(pts.ToArray());
                }

                Handles.Label(stopWorld, stops[s].stopName,
                    active ? EditorStyles.boldLabel : EditorStyles.miniLabel);
            }
        }
    }
}
