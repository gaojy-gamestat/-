using UnityEditor;
using UnityEngine;

namespace GameNet.Gameplay.EditorTools
{
    /// <summary>
    /// VillaRoomBuilder 的 Inspector：一键增删各类区域。
    ///
    /// 「＋ 添加地板/障碍物/墙壁/门」→ 在「别墅」图物体下创建子物体并挂对应组件；
    /// 「－ 删除选中区域」→ 删除当前在 Hierarchy 选中的那个区域物体。
    /// 增删后选中新建的区域，到 Scene 视图拖 4 个顶点即可摆放。
    /// </summary>
    [CustomEditor(typeof(VillaRoomBuilder))]
    [CanEditMultipleObjects]
    public class VillaRoomBuilderEditor : Editor
    {
        private int _floorCounter = 1;
        private int _obstacleCounter = 1;
        private int _wallCounter = 1;
        private int _doorCounter = 1;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var builder = (VillaRoomBuilder)target;

            GUILayout.Space(10);
            GUILayout.Label("区域管理（增删自由，多房间就多加几块地板）", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(Application.isPlaying);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("＋ 添加地板", EditorStyles.miniButtonLeft))
            {
                CreateZone<VillaFloorArea>(builder, "地板_" + _floorCounter++);
            }

            if (GUILayout.Button("＋ 添加障碍物", EditorStyles.miniButtonMid))
            {
                CreateZone<VillaObstacle>(builder, "障碍物_" + _obstacleCounter++);
            }

            if (GUILayout.Button("＋ 添加墙壁", EditorStyles.miniButtonMid))
            {
                CreateZone<VillaWall>(builder, "墙壁_" + _wallCounter++);
            }

            if (GUILayout.Button("＋ 添加门", EditorStyles.miniButtonRight))
            {
                CreateZone<VillaDoor>(builder, "门_" + _doorCounter++);
            }

            GUILayout.EndHorizontal();

            if (GUILayout.Button("－ 删除选中区域（Hierarchy 里选中的那个）"))
            {
                DeleteSelectedZone(builder);
            }

            if (GUILayout.Button("↻ 重建 / 刷新区域列表"))
            {
                builder.Rebuild();
                EditorUtility.SetDirty(builder);
            }

            EditorGUI.EndDisabledGroup();

            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "摆放流程：\n" +
                "1. 选中某个区域物体（地板/障碍物/墙/门）；\n" +
                "2. Scene 视图里拖 4 个白点（实际位置线框 = 碰撞体真实位置）；\n" +
                "3. 对照 Game 视图里的“投影位置线框”，让它盖住别墅图里的对应区域；\n" +
                "4. 门要指定 targetDoor（互相对指），spawnOffset 决定传送落点。",
                MessageType.Info);
        }

        private static void CreateZone<T>(VillaRoomBuilder builder, string name) where T : VillaZoneBase
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "添加 " + name);
            go.transform.SetParent(builder.transform, false);

            var zone = go.AddComponent<T>();
            // 新区域默认放在图中间偏下，别一上来就挡住别的区域
            Vector2 c = new Vector2(0.5f, 0.8f);
            Vector2 half = new Vector2(0.06f, 0.04f);
            zone.corners = new Vector2[]
            {
                c + new Vector2(-half.x, -half.y), c + new Vector2(half.x, -half.y),
                c + new Vector2(half.x, half.y), c + new Vector2(-half.x, half.y)
            };

            Selection.activeGameObject = go;
            builder.Rebuild();
            EditorUtility.SetDirty(builder);
        }

        private static void DeleteSelectedZone(VillaRoomBuilder builder)
        {
            var zone = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<VillaZoneBase>()
                : null;
            if (zone == null)
            {
                Debug.LogWarning("请先在 Hierarchy 里选中一个区域物体（地板/障碍物/墙壁/门）再删除。");
                return;
            }

            Undo.DestroyObjectImmediate(zone.gameObject);
            builder.Rebuild();
            EditorUtility.SetDirty(builder);
        }
    }
}
