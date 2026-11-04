using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameNet.Gameplay.EditorTools
{
    /// <summary>
    /// 家具三件套的一键工具：导入格式 / 对位 / 碰撞体 / 层级遮盖 / 检查。
    ///
    /// 用法（打开「第一关」场景）：
    ///   ① Project 里选中美术给的家具图 → 家具：① 选中图片 → 设成家具导入格式；
    ///   ② 把图片拖到 Hierarchy 的「别墅」物体下面；
    ///   ③ 选中这些家具 → 家具：③ 一键生成碰撞体（选中家具）；
    ///   ④ 再点 → 家具：④ 加入层级排序（选中家具）；
    ///   ⑤ Scene 视图里拖 4 个红点微调碰撞块 → Play 测试。
    ///
    /// 碰撞体怎么算出来的：
    ///   家具图片里「不透明的那块」= 家具本体 → 取它底边往上 Footprint Depth 比例的一条
    ///   地面投影带 → 换算成 VillaObstacle 的 4 个归一化顶点（左上/右上/右下/左下）。
    ///   （斜俯视里家具的脚就在图片底边，往上是它在地面上占的进深。）
    ///   美术无论给「裁紧的单件图」还是「整图尺寸 + 只留一件家具」的图，都能自动识别。
    /// </summary>
    public static class VillaFurnitureSetupTool
    {
        private const string MenuRoot = "Tools/心灵驾到/";

        // 自动算碰撞体时的几个可调默认值（改这里即可）
        // 家具在地面上占的进深 = 家具图高度 × 这个比例。
        // 碰撞带取家具可见区域底部 50%（座位段）。
        // 太细（25%）角色会踩进沙发靠背里；太满（65%+）会把沙发旁边的走道也堵死。
        // 个别家具（如靠墙大沙发）可在场景里手动调深。
        private const float FootprintDepth = 0.5f;
        private const float SideInset = 0.05f;       // 左右各内缩，避免碰撞块比家具宽
        private const float ObstacleHeight = 1.2f;   // 碰撞柱高度（角色撞到侧面被挡住）

        // ==================================================================
        // ⓪ 备用入口：自动在「别墅」下找全部家具，不需要手动选中
        // ==================================================================
        [MenuItem(MenuRoot + "家具：⓪ 一键全套（自动找，不用选中）", false, 9)]
        public static void AllInOneAuto()
        {
            RunAllInOne(false);
        }

        /// <summary>静默版：给「自动修复守护」调用。不弹窗；没有要修的东西就什么都不做。</summary>
        public static void AllInOneAutoSilent()
        {
            RunAllInOne(true);
        }

        /// <summary>
        /// 真正的一键全套。
        ///
        /// ⚠️ 关键设计：Unity 在 Play 模式里做的所有改动，退出 Play 时会被全部还原（等于白做）。
        /// 所以这里必须先拦住「在 Play 模式里跑工具」的情况，否则会反复出现“明明跑过了却没效果”。
        /// </summary>
        public static void RunAllInOne(bool silent)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (!silent)
                {
                    Warn("请先退出 Play 模式",
                        "Unity 在 Play 模式里做的所有改动，退出时都会被还原（等于白做）。\n\n" +
                        "请点一下顶部中间的 ▶ 停止运行，让它变成灰色，然后再点这个菜单。");
                }
                return;
            }

            RectTransform villa = FindVillaRoot(silent);
            if (villa == null) return;

            // 手动点菜单时：Project 里选中了 PNG → 直接生成 UI 家具；否则用别墅下已有的家具。
            // 静默（自动守护）时：只修复场景里已有的家具，
            // 不会因为你在 Project 里碰巧选中了图片就凭空新建一堆家具。
            List<RectTransform> list = silent
                ? FindAllFurniture(villa)
                : CreateFurnitureFromSelectedTextures(villa);
            if (list.Count == 0) list = FindAllFurniture(villa);

            if (list.Count == 0)
            {
                Warn("没找到家具",
                    "请先在 Project 窗口 Assets/Art 里，按住 Ctrl 选中 6 张 家具_*.png\n" +
                    "（就是 家具_中岛 / 家具_中岛凳 / 家具_单人椅 / 家具_床 / 家具_沙发 / 家具_茶几），\n" +
                    "然后再点这个菜单。\n\n" +
                    "注意：不要把图片拖进 Hierarchy 或 Scene，那样只会生成 Sprite Renderer。");
                return;
            }

            // 静默模式（自动守护）：如果尺寸位置本来就对，说明碰撞框可能已经手动调好了，
            // 就别再重算覆盖掉，直接跳过。
            if (silent && !NeedsFix(villa, list))
            {
                Debug.Log("[家具工具] 自动检查：" + list.Count + " 件家具尺寸与位置均正常，无需修复。");
                return;
            }

            Selection.objects = ToObjects(list);
            RestoreNativeSize();

            Selection.objects = ToObjects(list);
            GenerateColliders();

            Selection.objects = ToObjects(list);
            AddToSorter();

            var names = new List<string>();
            foreach (RectTransform rt in list) names.Add("√ " + rt.name);

            Debug.Log("[家具工具] ⓪ 已处理 " + list.Count + " 件家具：\n" + string.Join("\n", names.ToArray()));

            // 自动保存场景。这一步最容易忘：忘了 Ctrl+S，或在 Play 模式里跑的，结果都等于白做。
            AutoSaveScene();

            if (!silent)
            {
                EditorUtility.DisplayDialog("家具已处理",
                    "共 " + list.Count + " 件：\n" + string.Join("\n", names.ToArray()) +
                    "\n\n已自动保存场景。\n\n" +
                    "下一步：点 ▶ 运行，操控角色走到家具后面（家具应挡住角色）、" +
                    "再走到家具前面（角色应挡住家具）。",
                    "好的");
            }
        }

        /// <summary>
        /// 判断家具是否需要修复：正确的家具应该「和别墅图一样大、且完全居中」。
        /// 只要有一件偏移了或尺寸不对，就说明是刚拖进来的、还没处理过。
        /// </summary>
        private static bool NeedsFix(RectTransform villa, List<RectTransform> list)
        {
            VillaDepthSorter sorter = FindSorter();
            Vector2 design = GetVillaDesignSize(villa);

            foreach (RectTransform rt in list)
            {
                if (rt == null) continue;

                // 位置：正确应该正好在别墅图中心
                if (rt.anchoredPosition.sqrMagnitude > 1f) return true;

                // 尺寸：正确应该等于底图的设计尺寸（1920x1080）。
                // ⚠️ 不能和 villa.rect 比（实时值随窗口大小变）——
                // 比较结果忽真忽假，守护脚本会在错误时机把错误尺寸烤进场景。
                if (Mathf.Abs(rt.sizeDelta.x - design.x) > 2f) return true;
                if (Mathf.Abs(rt.sizeDelta.y - design.y) > 2f) return true;

                // 没挂碰撞体 / 没进排序器 → 说明还没处理过（例如之前的修复被 Play 模式还原了）
                if (rt.GetComponent<VillaObstacle>() == null) return true;
                if (sorter == null || !sorter.targets.Contains(rt)) return true;
            }

            return false;
        }

        /// <summary>自动保存当前场景（仅在确实有改动时保存，避免无谓写入）。</summary>
        public static void AutoSaveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isDirty)
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[家具工具] 场景已自动保存：" + scene.path);
            }
        }

        // ==================================================================
        // ① 图片导入格式（Project 里选中图片后用）
        // ==================================================================
        [MenuItem(MenuRoot + "家具：① 选中图片 → 设成家具导入格式", false, 10)]
        public static void SetImportSettings()
        {
            List<TextureImporter> list = GetSelectedImporters();
            if (list.Count == 0)
            {
                Warn("没选中图片", "请先在 Project 窗口里选中 1 张或多张家具 PNG，再点这个菜单。");
                return;
            }

            foreach (TextureImporter ti in list)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Uncompressed; // 防止透明边缘发黑
                ti.maxTextureSize = 4096;
                ti.isReadable = true;                                            // 让工具能扫描透明区域
                ti.spritePivot = new Vector2(0.5f, 0.5f);                        // 中心 Pivot
                ForceAllPlatformsUncompressed(ti);                                // 各平台覆盖段也要不压缩
                EditorUtility.SetDirty(ti);
                ti.SaveAndReimport();
            }

            Debug.Log("[家具工具] 已设置 " + list.Count + " 张图片的导入格式。");
        }

        // ==================================================================
        // ② 还原尺寸 + 自动对位（Hierarchy 里选中家具后用）
        // ==================================================================
        [MenuItem(MenuRoot + "家具：② 选中家具 → 还原尺寸 / 自动对位", false, 11)]
        public static void RestoreNativeSize()
        {
            RectTransform villa = FindVillaRoot();
            if (villa == null) return;

            List<RectTransform> list = GetSelectedFurniture();
            if (list.Count == 0)
            {
                Warn("没选中家具", "请先在 Hierarchy 里选中家具物体（可多选），再点这个菜单。");
                return;
            }

            foreach (RectTransform rt in list)
            {
                Image img = rt.GetComponent<Image>();
                if (img == null || img.sprite == null) continue;

                Undo.RecordObject(rt, "家具还原尺寸");
                img.SetNativeSize();

                // 整图尺寸的透明 PNG：正确尺寸 = 和底图一样大 + 居中，
                // 家具才刚好叠在底图原位上（SetNativeSize 按 PixelsPerUnit=100 会缩到 1/100，太小）
                if (IsDescendant(rt, villa))
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.localScale = Vector3.one;
                    rt.localRotation = Quaternion.identity;
                    rt.anchoredPosition = Vector2.zero;
                    // ⚠️ 必须写「设计尺寸」（底图 sprite 的原始像素尺寸，1920x1080），
                    // 不能写 villa.rect（那是当下 Game 视图的实时尺寸，会随窗口大小变）。
                    // 之前写成 villa.rect，守护脚本在大窗口/异常画布的瞬间跑了一次，
                    // 把 4988x2806 烤进了场景并自动保存 → 家具全变成 2.6 倍巨大（大床 bug）。
                    rt.sizeDelta = GetVillaDesignSize(villa);
                }

                EditorUtility.SetDirty(rt);
            }

            MarkDirty();
            Debug.Log("[家具工具] 已还原 " + list.Count + " 件家具的尺寸。");
        }

        // ==================================================================
        // ③ 一键生成碰撞体
        // ==================================================================
        [MenuItem(MenuRoot + "家具：③ 一键生成碰撞体（选中家具）", false, 12)]
        public static void GenerateColliders()
        {
            RectTransform villa = FindVillaRoot();
            if (villa == null) return;

            List<RectTransform> list = GetSelectedFurniture();
            if (list.Count == 0)
            {
                Warn("没选中家具", "请先在 Hierarchy 里选中家具物体（可多选），再点这个菜单。");
                return;
            }

            foreach (RectTransform rt in list)
            {
                // 1. 必须挂在别墅图下面，否则碰撞体算不出世界坐标
                if (!IsDescendant(rt, villa))
                {
                    Undo.SetTransformParent(rt, villa, "家具移到底图下");
                }

                // 2. 加/更新 VillaObstacle，自动算 4 个顶点
                VillaObstacle ob = rt.GetComponent<VillaObstacle>();
                if (ob == null)
                {
                    ob = Undo.AddComponent<VillaObstacle>(rt.gameObject);
                }
                else
                {
                    Undo.RecordObject(ob, "更新家具碰撞体");
                }

                Vector2[] corners = CalcFootprintCorners(villa, rt);
                if (corners == null)
                {
                    // 扫描失败时不要盲目删碰撞体：
                    // 如果这件家具已经有算好的、尺寸合理的碰撞体（例如离线写入的），
                    // 一律保留；只删「刚新加、还带着默认全屏大框」的，那种留着会堵死角色。
                    bool hasReasonable = ob.corners != null && ob.corners.Length == 4
                        && (ob.corners[1].x - ob.corners[0].x) > 0f
                        && (ob.corners[1].x - ob.corners[0].x) < 0.5f;
                    if (!hasReasonable)
                    {
                        Undo.DestroyObjectImmediate(ob);
                    }
                    continue;
                }

                ob.corners = corners;
                ob.height = ObstacleHeight;
                EditorUtility.SetDirty(ob);

                // 3. 家具是纯装饰：关掉 Raycast Target，免得挡住点击
                Image img = rt.GetComponent<Image>();
                if (img != null)
                {
                    Undo.RecordObject(img, "家具关闭射线");
                    img.raycastTarget = false;
                    EditorUtility.SetDirty(img);
                }
            }

            MarkDirty();
            Selection.objects = ToObjects(list);
            Debug.Log("[家具工具] 已为 " + list.Count + " 件家具生成碰撞体（红色四点可直接拖）。");
        }

        // ==================================================================
        // ④ 加入层级排序
        // ==================================================================
        [MenuItem(MenuRoot + "家具：④ 加入层级排序（选中家具）", false, 13)]
        public static void AddToSorter()
        {
            RectTransform villa = FindVillaRoot();
            if (villa == null) return;

            List<RectTransform> list = GetSelectedFurniture();
            if (list.Count == 0)
            {
                Warn("没选中家具", "请先在 Hierarchy 里选中家具物体（可多选），再点这个菜单。");
                return;
            }

            VillaDepthSorter sorter = FindSorter();
            if (sorter == null)
            {
                sorter = Undo.AddComponent<VillaDepthSorter>(villa.gameObject);
            }
            Undo.RecordObject(sorter, "加入家具排序");

            int added = 0;
            foreach (RectTransform rt in list)
            {
                if (sorter.targets.Contains(rt)) continue;
                sorter.targets.Add(rt);
                added++;
            }

            EditorUtility.SetDirty(sorter);
            MarkDirty();
            Debug.Log("[家具工具] 已把 " + added + " 件家具加入排序器（挂在「" + villa.name + "」上）。");
        }

        // ==================================================================
        // ⑤ 一键全套
        // ==================================================================
        [MenuItem(MenuRoot + "家具：⑤ 一键全套（碰撞 + 排序）", false, 14)]
        public static void AllInOne()
        {
            GenerateColliders();
            AddToSorter();
        }

        // ==================================================================
        // ⑥ 检查状态
        // ==================================================================
        [MenuItem(MenuRoot + "家具：⑥ 检查家具状态", false, 15)]
        public static void Check()
        {
            RectTransform villa = FindVillaRoot();
            if (villa == null) return;

            VillaDepthSorter sorter = FindSorter();
            var lines = new List<string>();

            int withObstacle = 0;
            int inSorter = 0;
            int total = 0;

            for (int i = 0; i < villa.childCount; i++)
            {
                Transform child = villa.GetChild(i);
                Image img = child.GetComponent<Image>();
                if (img == null) continue;

                total++;
                bool hasOb = child.GetComponent<VillaObstacle>() != null;
                bool inS = sorter != null && sorter.targets.Contains(child);
                if (hasOb) withObstacle++;
                if (inS) inSorter++;

                string mark = (hasOb && inS) ? "√ " : "× ";
                lines.Add(mark + child.name
                          + "  碰撞体:" + (hasOb ? "有" : "无")
                          + "  排序:" + (inS ? "有" : "无")
                          + (img.raycastTarget ? "  【射线未关】" : ""));
            }

            string report = "排序器: " + (sorter != null ? "有（挂在「" + sorter.gameObject.name + "」上）" : "无")
                           + "\n家具总数: " + total
                           + " / 有碰撞体: " + withObstacle
                           + " / 已排序: " + inSorter
                           + "\n自动生成参数：进深 " + FootprintDepth.ToString("0.00")
                           + " / 高度 " + ObstacleHeight.ToString("0.0")
                           + " / 内缩 " + SideInset.ToString("0.00")
                           + "\n\n"
                           + (lines.Count > 0 ? string.Join("\n", lines.ToArray()) : "（别墅图下面还没有家具物体）");

            Debug.Log("[家具工具] 检查报告\n" + report);
            EditorUtility.DisplayDialog("家具状态检查", report, "知道了");
        }

        // ==================================================================
        // 菜单可用性
        // ==================================================================
        [MenuItem(MenuRoot + "家具：① 选中图片 → 设成家具导入格式", true)]
        public static bool ValidateTextures()
        {
            return GetSelectedImporters().Count > 0;
        }

        [MenuItem(MenuRoot + "家具：② 选中家具 → 还原尺寸 / 自动对位", true)]
        [MenuItem(MenuRoot + "家具：③ 一键生成碰撞体（选中家具）", true)]
        [MenuItem(MenuRoot + "家具：④ 加入层级排序（选中家具）", true)]
        [MenuItem(MenuRoot + "家具：⑤ 一键全套（碰撞 + 排序）", true)]
        public static bool ValidateFurniture()
        {
            return GetSelectedFurniture().Count > 0;
        }

        // ==================================================================
        // 内部工具
        // ==================================================================

        /// <summary>
        /// 把所有「平台覆盖段」也强制设成不压缩。
        ///
        /// 坑：ti.textureCompression 只改 DefaultTexturePlatform 一段。
        /// Windows 编辑器实际生效的是 Standalone 覆盖段，而 Unity 给新图生成的
        /// Standalone / WebGL / Android / iPhone 段默认都是 textureCompression: 1（压缩）。
        /// 压缩纹理上 Texture2D.GetPixels32() 会抛异常 → 透明区域扫描失败 →
        /// 碰撞体被整批删掉（日志里 17 条「扫描失败」就是这么来的）。
        /// </summary>
        private static void ForceAllPlatformsUncompressed(TextureImporter ti)
        {
            string[] platforms = { "Standalone", "WebGL", "Android", "iPhone", "Server" };
            foreach (string p in platforms)
            {
                TextureImporterPlatformSettings s = ti.GetPlatformTextureSettings(p);
                s.overridden = true;
                s.maxTextureSize = 4096;
                s.textureCompression = TextureImporterCompression.Uncompressed;
                s.crunchedCompression = false;
                ti.SetPlatformTextureSettings(s);
            }
        }

        private static List<TextureImporter> GetSelectedImporters()
        {
            var result = new List<TextureImporter>();
            foreach (Object o in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path)) continue;
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null) result.Add(ti);
            }

            return result;
        }

        private static List<RectTransform> GetSelectedFurniture()
        {
            var result = new List<RectTransform>();
            foreach (GameObject go in Selection.gameObjects)
            {
                RectTransform rt = go.transform as RectTransform;
                if (rt == null) continue;
                if (rt.GetComponent<Image>() == null) continue;
                result.Add(rt);
            }

            return result;
        }

        /// <summary>
        /// 把 Project 里选中的家具 PNG 直接生成为 UI Image 家具，挂到别墅图下面。
        /// 如果同名子物体已经是正确的 Image → 复用；如果是 Sprite Renderer（拖错生成的）→ 删掉重建。
        /// </summary>
        private static List<RectTransform> CreateFurnitureFromSelectedTextures(RectTransform villa)
        {
            var result = new List<RectTransform>();

            foreach (TextureImporter ti in GetSelectedImporters())
            {
                string path = ti.assetPath;
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("家具")) continue;

                // 新拷进项目里的 PNG 默认是 Texture 类型，既拖不进 UI 也生成不了家具。
                // 这里顺手转成 Sprite(2D and UI) + 开 Read/Write，省得再单独跑一遍 ①。
                // Read/Write 不开的话透明区域扫描会失败，碰撞框就会出错。
                if (ti.textureType != TextureImporterType.Sprite || !ti.isReadable ||
                    ti.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.alphaIsTransparency = true;
                    ti.mipmapEnabled = false;
                    ti.isReadable = true;
                    // 关键：压缩纹理（DXT/ETC）上 GetPixels32 会抛异常，
                    // 导致透明区域扫描失败、碰撞体被整批删掉。必须强制不压缩。
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    // 只设默认段不够！Windows 编辑器实际用的是 Standalone 平台覆盖段，
                    // WebGL/Android 等段也各自带 textureCompression: 1。
                    // 必须逐段改成 Uncompressed，否则 GetPixels32 仍然失败。
                    ForceAllPlatformsUncompressed(ti);
                    ti.SaveAndReimport();
                }

                Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sp == null) continue;

                GameObject go;
                Transform exist = villa.Find(name);
                if (exist != null && exist.GetComponent<Image>() != null)
                {
                    go = exist.gameObject;
                }
                else
                {
                    if (exist != null) Undo.DestroyObjectImmediate(exist.gameObject);
                    go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    Undo.RegisterCreatedObjectUndo(go, "生成家具");
                    Undo.SetTransformParent(go.transform, villa, "家具挂到底图下");
                }

                Image img = go.GetComponent<Image>();
                Undo.RecordObject(img, "设置家具图片");
                img.sprite = sp;
                img.raycastTarget = false;
                EditorUtility.SetDirty(img);

                RectTransform rt = go.transform as RectTransform;
                rt.SetAsLastSibling();
                result.Add(rt);
            }

            return result;
        }

        /// <summary>在别墅图底下找所有家具（名字以「家具」开头 + 带 Image），跳过地板/墙体。</summary>
        private static List<RectTransform> FindAllFurniture(RectTransform villa)
        {
            var result = new List<RectTransform>();
            Image[] imgs = villa.GetComponentsInChildren<Image>(true);
            foreach (Image img in imgs)
            {
                RectTransform rt = img.transform as RectTransform;
                if (rt == null || rt == villa) continue;
                if (rt.name.StartsWith("家具")) result.Add(rt);
            }

            return result;
        }

        private static Object[] ToObjects(List<RectTransform> list)
        {
            var arr = new Object[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i].gameObject;
            return arr;
        }

        /// <summary>找到别墅图（上面挂着 VillaRoomBuilder 的那个 RectTransform）。</summary>
        private static RectTransform FindVillaRoot(bool silent = false)
        {
            VillaRoomBuilder[] builders = Object.FindObjectsOfType<VillaRoomBuilder>(true);
            if (builders.Length == 0)
            {
                // 静默模式（自动修复守护）：不打扰用户，只在 Console 留一行说明
                if (silent) return null;

                Warn("场景里没有别墅图",
                    "没找到挂了 VillaRoomBuilder 的物体。\n" +
                    "请确认当前打开的是「第一关」场景，且别墅图上已挂 VillaRoomBuilder\n" +
                    "（没有的话先跑一次 Tools/心灵驾到/初始化别墅行走区域）。");
                return null;
            }

            RectTransform rt = builders[0].transform as RectTransform;
            if (rt == null)
            {
                Warn("别墅图不在 UI 上", "别墅图物体没有 RectTransform，家具排序需要它在 Canvas 下。");
                return null;
            }

            return rt;
        }

        private static VillaDepthSorter FindSorter()
        {
            VillaDepthSorter[] list = Object.FindObjectsOfType<VillaDepthSorter>(true);
            return list.Length > 0 ? list[0] : null;
        }

        /// <summary>
        /// 底图的「设计尺寸」= 别墅 sprite 的原始像素尺寸（1920x1080）。
        /// 家具是和底图同尺寸的全画布透明叠加层，所以家具的正确 sizeDelta 永远等于它，
        /// 而不是 villa.rect（实时值，随 Game 视图窗口大小变化，烤进场景就是大床 bug）。
        /// </summary>
        private static Vector2 GetVillaDesignSize(RectTransform villa)
        {
            Image villaImg = villa != null ? villa.GetComponent<Image>() : null;
            if (villaImg != null && villaImg.sprite != null &&
                villaImg.sprite.rect.width > 1f && villaImg.sprite.rect.height > 1f)
            {
                return new Vector2(villaImg.sprite.rect.width, villaImg.sprite.rect.height);
            }

            return new Vector2(1920f, 1080f); // 兜底：新别墅底图的设计尺寸
        }

        private static bool IsDescendant(Transform t, Transform parent)
        {
            Transform p = t.parent;
            while (p != null)
            {
                if (p == parent) return true;
                p = p.parent;
            }

            return false;
        }

        /// <summary>
        /// 家具图 → 地面碰撞四边形（别墅图归一化坐标）。
        /// 先找出图片里不透明的那块（家具本体），再取它底边往上 FootprintDepth 的一条带。
        /// </summary>
        private static Vector2[] CalcFootprintCorners(RectTransform villa, RectTransform furniture)
        {
            Rect uv = GetVisibleUVRect(furniture.GetComponent<Image>());

            // 扫描失败（纹理不可读等）会退回整图 → 碰撞体会覆盖整个画面底部，把角色堵死。
            // 这里直接判失败，宁可不生成碰撞体，也不能生成一个全屏大框。
            if (uv.width * uv.height > 0.8f)
            {
                Debug.LogWarning("[家具工具] " + furniture.name +
                    " 的图片透明区域扫描失败（可能没开 Read/Write），跳过它的碰撞体。请重新跑一次 ①。");
                return null;
            }

            // 只有「家具 rect 和底图一样大」时归一化换算才成立。
            // 尺寸不一致 = 家具还没对位 → 拒绝生成碰撞体，
            // 免得算出覆盖全屏的大框把角色堵死。
            if (Mathf.Abs(furniture.rect.width - villa.rect.width) > 2f ||
                Mathf.Abs(furniture.rect.height - villa.rect.height) > 2f)
            {
                Debug.LogWarning("[家具工具] " + furniture.name +
                    " 尺寸(" + furniture.rect.width.ToString("F0") + "x" + furniture.rect.height.ToString("F0") +
                    ")与底图(" + villa.rect.width.ToString("F0") + "x" + villa.rect.height.ToString("F0") +
                    ")不一致，跳过碰撞体。请重新跑一次 ⓪。");
                return null;
            }

            // 家具 RectTransform 的本地矩形（矩形左下 + 尺寸）
            Rect r = furniture.rect;

            // 可见区域在家具本地矩形里的左下 / 右上（本地坐标，原点 = pivot）
            float lx0 = r.x + uv.x * r.width;
            float ly0 = r.y + uv.y * r.height;
            float lx1 = lx0 + uv.width * r.width;
            float ly1 = ly0 + uv.height * r.height;

            // 转成别墅图的本地坐标
            Vector3 a = villa.InverseTransformPoint(furniture.TransformPoint(new Vector3(lx0, ly0, 0f)));
            Vector3 b = villa.InverseTransformPoint(furniture.TransformPoint(new Vector3(lx1, ly1, 0f)));
            Rect vr = villa.rect;

            float u0 = (a.x - vr.xMin) / vr.width;      // 左
            float u1 = (b.x - vr.xMin) / vr.width;      // 右
            float v0 = (a.y - vr.yMin) / vr.height;     // 下（家具的脚）
            float v1 = (b.y - vr.yMin) / vr.height;     // 上

            float uw = u1 - u0;
            float vh = v1 - v0;
            u0 += uw * SideInset;
            u1 -= uw * SideInset;
            float vTop = v0 + vh * FootprintDepth;      // 图片往上 = 地面往里

            // VillaZoneBase 约定：x 左→右，y 上→下（左上为原点），顺序 左上/右上/右下/左下
            var corners = new Vector2[4];
            corners[0] = new Vector2(u0, 1f - vTop);
            corners[1] = new Vector2(u1, 1f - vTop);
            corners[2] = new Vector2(u1, 1f - v0);
            corners[3] = new Vector2(u0, 1f - v0);
            return corners;
        }

        /// <summary>扫描精灵纹理里「不透明像素」的范围，返回它在精灵内的 UV 区间（左下原点）。扫不到就返回整块。</summary>
        private static Rect GetVisibleUVRect(Image img)
        {
            if (img == null || img.sprite == null) return new Rect(0f, 0f, 1f, 1f);

            Sprite s = img.sprite;
            Texture2D tex = s.texture;
            if (tex == null) return new Rect(0f, 0f, 1f, 1f);

            try
            {
                Rect tr = s.textureRect;   // 精灵在纹理里的像素范围（左下原点）
                int x0 = Mathf.Max(0, (int)tr.xMin);
                int y0 = Mathf.Max(0, (int)tr.yMin);
                int w = Mathf.Clamp((int)tr.width, 1, tex.width - x0);
                int h = Mathf.Clamp((int)tr.height, 1, tex.height - y0);

                Color32[] px = tex.GetPixels32();
                int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;

                for (int y = y0; y < y0 + h; y++)
                {
                    int row = y * tex.width;
                    for (int x = x0; x < x0 + w; x++)
                    {
                        if (px[row + x].a <= 8) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }

                if (maxX < 0) return new Rect(0f, 0f, 1f, 1f);

                // 转回「相对于精灵自身」的 UV
                float ux = (minX - x0) / (float)w;
                float uy = (minY - y0) / (float)h;
                float uw = (maxX - minX + 1) / (float)w;
                float uh = (maxY - minY + 1) / (float)h;
                return new Rect(ux, uy, uw, uh);
            }
            catch (System.Exception)
            {
                // 纹理没开 Read/Write Enabled 就会读失败 → 退回整块矩形
                return new Rect(0f, 0f, 1f, 1f);
            }
        }

        private static void MarkDirty()
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        private static void Warn(string title, string msg)
        {
            EditorUtility.DisplayDialog(title, msg, "知道了");
        }
    }
}
