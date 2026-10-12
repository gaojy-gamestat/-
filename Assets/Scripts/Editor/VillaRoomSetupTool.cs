using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameNet.Gameplay.EditorTools
{
    /// <summary>
    /// 一键把「第一关」这样的别墅场景初始化成新的行走区域系统：
    /// 1. 在「别墅」图物体上挂 VillaRoomBuilder；
    /// 2. 把旧的 VillaGroundPlane 地板范围（floorLeftTop/floorRightBottom）
    ///    转换成第一块 VillaFloorArea（四点可拖拽四边形）；
    /// 3. 移除旧的 VillaGroundPlane 组件（新系统全面接管，角色脚本自动改走新系统）。
    ///
    /// 用法：打开第一关场景 → 菜单 Tools/心灵驾到/初始化别墅行走区域 → 保存场景。
    /// </summary>
    public static class VillaRoomSetupTool
    {
        [MenuItem("Tools/心灵驾到/初始化别墅行走区域")]
        public static void Setup()
        {
            // ---------- 1. 找到别墅图 ----------
            GameObject villaGo = GameObject.Find("别墅");
            if (villaGo == null)
            {
                EditorUtility.DisplayDialog("找不到别墅图",
                    "场景里没有名为「别墅」的物体。\n请确认当前打开的是带别墅背景图的场景（例如 第一关）。", "知道了");
                return;
            }

            Image villaImage = villaGo.GetComponent<Image>();
            if (villaImage == null)
            {
                EditorUtility.DisplayDialog("别墅物体上没有 Image",
                    "「别墅」物体上没有 Image 组件，无法做图坐标换算。", "知道了");
                return;
            }

            // ---------- 2. 挂 VillaRoomBuilder ----------
            VillaRoomBuilder builder = villaGo.GetComponent<VillaRoomBuilder>();
            if (builder == null)
            {
                builder = Undo.AddComponent<VillaRoomBuilder>(villaGo);
                builder.villaImage = villaImage;
                Debug.Log("[别墅房间] 已在「别墅」上创建 VillaRoomBuilder。");
            }

            // ---------- 3. 转换旧 VillaGroundPlane 的地板范围 ----------
            VillaGroundPlane oldGround = villaGo.GetComponent<VillaGroundPlane>();
            bool converted = false;
            if (oldGround != null)
            {
                builder.groundHeight = oldGround.groundHeight;

                var floorGo = new GameObject("地板_1");
                Undo.RegisterCreatedObjectUndo(floorGo, "创建 地板_1");
                floorGo.transform.SetParent(villaGo.transform, false);

                var floor = floorGo.AddComponent<VillaFloorArea>();
                // 旧的左上/右下矩形 → 4 顶点四边形（0=左上 1=右上 2=右下 3=左下）
                Vector2 lt = oldGround.floorLeftTop;
                Vector2 rb = oldGround.floorRightBottom;
                floor.corners = new Vector2[]
                {
                    new Vector2(lt.x, lt.y), new Vector2(rb.x, lt.y),
                    new Vector2(rb.x, rb.y), new Vector2(lt.x, rb.y)
                };
                floor.padding = oldGround.floorPadding;
                converted = true;

                // 旧组件退役（新系统接管；角色脚本会自动优先使用 VillaRoomBuilder）
                Undo.DestroyObjectImmediate(oldGround);
                Debug.Log("[别墅房间] 已把 VillaGroundPlane 的地板范围转成「地板_1」并移除旧组件。");
            }

            builder.Rebuild();
            EditorUtility.SetDirty(builder);
            EditorUtility.SetDirty(villaGo);
            EditorSceneManagerMarkDirty();

            EditorUtility.DisplayDialog("初始化完成",
                (converted ? "已创建第一块地板「地板_1」（保留了原来的地板范围）。\n" : "VillaRoomBuilder 已就绪。\n") +
                "\n接下来：\n" +
                "· 选中「别墅」下的区域物体，在 Scene 视图拖 4 个白点调整；\n" +
                "· 对照 Game 视图的投影线框对齐图里的地板；\n" +
                "· 用 VillaRoomBuilder 的「＋ 添加…」按钮加更多房间/障碍物/墙/门。\n" +
                "· 记得 Ctrl+S 保存场景。", "知道了");
        }

        /// <summary>把当前场景标记为“有改动”，避免忘记保存。</summary>
        private static void EditorSceneManagerMarkDirty()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            }
        }
    }
}
