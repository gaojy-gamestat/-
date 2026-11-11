using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GameNet.Gameplay;

namespace GameNet.Gameplay.EditorTools
{
    /// <summary>
    /// 第一关清理工具（别墅图更新后用）：
    ///   1. 删除「别墅」下所有家具物体（名字以「家具」开头、或挂着 VillaObstacle 的子物体）
    ///      —— 这些是旧地图时代生成的透明家具图 + 红色碰撞框，新地图已对不上；
    ///   2. 删除场景里所有 VillaDepthSorter（层级遮盖排序器）；
    ///   3. 兜底清掉残留的 VillaObstacle 组件。
    ///
    /// 保留不动的：VillaRoomBuilder（别墅上的房间管理器）、地板/墙/门（VillaFloorArea /
    /// VillaWall / VillaDoor）、玩家、杜克、HUD 等一切其他东西。
    /// Project 里的家具 PNG 素材也不删，以后重新摆家具时还能用。
    ///
    /// 用法：打开第一关 → 菜单 Tools/心灵驾到/清理：删除家具物体和层级排序 → 确认 → 完成（自动保存场景）。
    /// </summary>
    public static class VillaDepthCleanupTool
    {
        [MenuItem("Tools/心灵驾到/清理：删除家具物体和层级排序")]
        public static void Cleanup()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("请在编辑模式运行",
                    "当前处于 Play 模式，改动会被还原。\n请先停止运行，再执行本菜单。", "知道了");
                return;
            }

            VillaRoomBuilder[] builders = Object.FindObjectsOfType<VillaRoomBuilder>(true);
            if (builders.Length == 0)
            {
                EditorUtility.DisplayDialog("找不到别墅",
                    "场景里没有挂 VillaRoomBuilder 的物体。\n请确认当前打开的是第一关场景。", "知道了");
                return;
            }

            Transform villa = builders[0].transform;

            // ---------- 统计将要删除的东西，先给用户过目 ----------
            var toDelete = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < villa.childCount; i++)
            {
                Transform child = villa.GetChild(i);
                bool isFurniture = child.name.StartsWith("家具");
                bool hasObstacle = child.GetComponent<VillaObstacle>() != null;
                if (isFurniture || hasObstacle)
                {
                    toDelete.Add(child);
                }
            }

            VillaDepthSorter[] sorters = Object.FindObjectsOfType<VillaDepthSorter>(true);

            string summary = "将要删除：\n" +
                             "  · 家具物体 " + toDelete.Count + " 个（别墅下的透明家具图 + 红色碰撞框）\n" +
                             "  · VillaDepthSorter 层级排序器 " + sorters.Length + " 个\n\n" +
                             "保留不动：房间管理器、地板、墙、门、玩家、杜克、HUD。\n" +
                             "Project 里的家具 PNG 素材不删。\n\n确定执行吗？";
            if (!EditorUtility.DisplayDialog("清理第一关家具遮盖", summary, "执行", "取消"))
            {
                return;
            }

            // ---------- 执行 ----------
            foreach (Transform t in toDelete)
            {
                Debug.Log("[清理] 删除家具物体：" + t.name);
                Undo.DestroyObjectImmediate(t.gameObject);
            }

            foreach (VillaDepthSorter sorter in sorters)
            {
                Debug.Log("[清理] 删除层级排序器（挂在 " + sorter.gameObject.name + " 上）");
                Undo.DestroyObjectImmediate(sorter);
            }

            // 兜底：极少数 VillaObstacle 不在别墅子树里（挂别的物体上）的，清掉组件
            VillaObstacle[] leftover = Object.FindObjectsOfType<VillaObstacle>(true);
            foreach (VillaObstacle ob in leftover)
            {
                Debug.Log("[清理] 清除残留碰撞组件（" + ob.gameObject.name + "）");
                Undo.DestroyObjectImmediate(ob);
            }

            // ---------- 保存 ----------
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isDirty)
            {
                EditorSceneManager.SaveScene(scene);
            }

            EditorUtility.DisplayDialog("清理完成",
                "已删除 " + toDelete.Count + " 个家具物体和 " + sorters.Length + " 个层级排序器，场景已保存。\n\n" +
                "下一步：重新对齐地板/墙/门的四边形（Scene 视图拖顶点），然后重新摆放家具。", "好的");
        }
    }
}
