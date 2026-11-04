using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 别墅家具 / 角色 的「前后遮挡排序器」（Y 轴层级遮盖）。
    ///
    /// ============================ 解决什么问题 ============================
    /// 角色走到桌子后面 → 桌子盖住角色；走到桌子前面 → 角色盖住桌子。
    ///
    /// ============================ 挂在哪里 ============================
    /// 挂到「别墅」图物体（或 Canvas）上即可。用菜单
    /// Tools/心灵驾到/家具：② 加入层级排序（选中家具） 会自动创建并接线。
    ///
    /// ============================ 怎么用 ============================
    /// 1. 家具图做成 UI Image，放在「别墅」图物体下面当子物体；
    /// 2. 把家具一个个拖进 Targets 列表（或勾着 Auto Collect Furniture 让它自动收集）；
    /// 3. 角色不用拖 —— Auto Include Character 会自动找到运行时生成的
    ///    「角色镜像(运行时生成)」并一起参与排序；
    /// 4. 按 Play，角色走到桌子上下看效果。
    ///
    /// ============================ 原理 ============================
    /// 每帧算每个物体“脚底”在画面上的高度：越靠画面下方（离观众越近）→ 排序值越大 → 画得越靠上。
    /// UI 物体通过「自带一层 overrideSorting 子 Canvas」改变排序，不改 Hierarchy 顺序，
    /// 所以不会把 HUD 面板的层级挤乱。
    ///
    /// ============================ 层级数值约定 ============================
    /// 底图（别墅整图）永远垫在最下面（Backdrop Order，默认 -20000）；
    /// 家具与角色在 Furniture Base 附近上下浮动（负数，默认 -100 → 永远被 HUD 盖住）；
    /// HUD 面板留在根 Canvas 里（0）→ 永远在最上面。
    /// 只在 Play 运行时加组件，编辑态不碰场景，避免污染场景文件。
    /// </summary>
    [ExecuteAlways]
    public class VillaDepthSorter : MonoBehaviour
    {
        [Header("参与排序的物体（家具 / 道具）")]
        public List<Transform> targets = new List<Transform>();

        [Header("自动收集（新手推荐全勾）")]
        [Tooltip("自动把底图（别墅图）下面所有带 Image 的直接子物体都当家具参与排序，不用一个个拖。")]
        public bool autoCollectFurniture = true;
        [Tooltip("自动找到角色运行时生成的 UI 镜像，一起参与排序。")]
        public bool autoIncludeCharacter = true;
        [Tooltip("VillaCharacterWalker 运行时生成的 UI 镜像名字，一般不用改。")]
        public string characterMirrorName = "角色镜像(运行时生成)";

        [Header("底图（别墅整图）")]
        [Tooltip("留空 = 自动查找场景里挂了 VillaRoomBuilder 的那个物体。")]
        public RectTransform backdrop;
        [Tooltip("底图永远垫在最下面，别改成正数。")]
        public int backdropOrder = -20000;

        [Header("排序参数")]
        [Tooltip("画面像素 → 排序值 的换算倍率。越大越精细。默认即可。")]
        public float pixelsToOrder = 0.1f;
        [Tooltip("家具/角色的排序基准。保持负数 → HUD 永远盖在家具上面。")]
        public int furnitureBase = -100;

        [Header("调试（遮挡没生效时打开看 Console）")]
        [Tooltip("运行时定期打印排序表：谁排前面、脚底 Y 是多少。")]
        public bool debugLog = true;
        public float debugInterval = 1.5f;
        private float _debugTimer;

        [Header("排序区间 / 方向")]
        [Tooltip("底图之上分配给家具和角色的排序值跨度。越大区分越细。")]
        public int orderSpan = 10000;
        [Tooltip("如果发现遮挡方向反了（人在家具前面却被挡住），把这个勾上。")]
        public bool invert = false;

        private float _backdropBottomY;
        private float _backdropTopY;

        private RectTransform _backdrop;
        private RectTransform _backdropRT;
        private Canvas _backdropCanvas;
        private Transform _characterUI;
        private VillaCharacterWalker _walker;
        private readonly List<Entry> _entries = new List<Entry>();

        private struct Entry
        {
            public Transform t;
            public RectTransform rt;
            public SpriteRenderer sr;
            public Canvas own;
            public float key;
        }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------
        private void LateUpdate()
        {
            // 编辑态不加组件、不改排序，避免把场景文件弄脏
            if (!Application.isPlaying) return;

            Collect();
            UpdateKeys();
            ApplyOrder();
        }

        // ------------------------------------------------------------------
        // 收集
        // ------------------------------------------------------------------
        private void Collect()
        {
            _entries.Clear();
            ResolveBackdrop();

            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null) AddEntry(targets[i]);
            }

            if (autoCollectFurniture && _backdropRT != null)
            {
                for (int i = 0; i < _backdropRT.childCount; i++)
                {
                    Transform child = _backdropRT.GetChild(i);
                    if (child.GetComponent<Image>() == null) continue;
                    if (targets.Contains(child)) continue;
                    AddEntry(child);
                }
            }

            if (autoIncludeCharacter) TryAddCharacter();
        }

        private void ResolveBackdrop()
        {
            if (_backdrop == null && backdrop != null) _backdrop = backdrop;

            if (_backdrop == null)
            {
                VillaRoomBuilder builder = FindObjectOfType<VillaRoomBuilder>();
                _backdrop = builder != null ? builder.transform as RectTransform : null;
            }

            _backdropRT = _backdrop;

            if (_backdropRT != null)
            {
                Vector3[] c = new Vector3[4];
                _backdropRT.GetWorldCorners(c);
                _backdropBottomY = (c[0].y + c[3].y) * 0.5f;
                _backdropTopY = (c[1].y + c[2].y) * 0.5f;
            }

            if (_backdropRT != null && Application.isPlaying)
            {
                _backdropCanvas = EnsureCanvas(_backdropRT.gameObject);
                _backdropCanvas.overrideSorting = true;
                _backdropCanvas.sortingOrder = backdropOrder;
            }
        }

        private void TryAddCharacter()
        {
            if (_characterUI == null)
            {
                GameObject found = GameObject.Find(characterMirrorName);
                if (found == null)
                {
                    // 兜底：名字里带「角色镜像」的 UI Image 都算
                    Image[] imgs = FindObjectsOfType<Image>(true);
                    for (int i = 0; i < imgs.Length; i++)
                    {
                        if (imgs[i] != null && imgs[i].gameObject.name.Contains("角色镜像"))
                        {
                            found = imgs[i].gameObject;
                            break;
                        }
                    }
                }
                if (found != null) _characterUI = found.transform;
            }
            if (_walker == null) _walker = FindObjectOfType<VillaCharacterWalker>();

            if (_characterUI != null)
            {
                AddEntry(_characterUI);           // UI 镜像（现在这套画面主要看它）
            }
            else if (_walker != null)
            {
                AddEntry(_walker.transform);      // 纯世界空间画法时的兜底
            }
        }

        private void AddEntry(Transform t)
        {
            if (t == _backdropRT) return;         // 底图自己不参与排序

            // 同一个物体可能被两条路径重复登记（角色镜像现在挂在别墅下面，
            // 会被「家具扫描」和「角色查找」各收一次），这里保证只算一次。
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].t == t) return;
            }

            Entry e;
            e.t = t;
            e.rt = t as RectTransform;
            e.sr = e.rt == null ? t.GetComponent<SpriteRenderer>() : null;
            if (e.rt == null && e.sr == null) return;

            e.own = e.rt != null ? EnsureCanvas(t.gameObject) : null;
            if (e.own != null) e.own.overrideSorting = true;
            e.key = 0f;
            _entries.Add(e);
        }

        private Canvas EnsureCanvas(GameObject go)
        {
            Canvas c = go.GetComponent<Canvas>();
            if (c == null) c = go.AddComponent<Canvas>();

            // ⚠️ 关键：子 Canvas 必须开启 overrideSorting，sortingOrder 才会真正生效。
            // Unity 的规则是：嵌套 Canvas 默认跟随父 Canvas 的排序，
            // 不打开 overrideSorting 的话，下面赋的 sortingOrder 会被完全忽略。
            // 表现就是「排序值算得全对，但家具死活挡不住人」。
            if (!c.overrideSorting)
            {
                c.overrideSorting = true;
            }

            return c;
        }

        // ------------------------------------------------------------------
        // 计算排序 key
        // ------------------------------------------------------------------
        private void UpdateKeys()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                e.key = GetBaselineScreenY(e);
                _entries[i] = e;
            }
        }

        /// <summary>
        /// 物体“脚底”的画面 Y。数值越大 = 越靠画面上方 = 越远 = 应该画得越靠后。
        /// </summary>
        private float GetBaselineScreenY(Entry e)
        {
            if (e.sr != null)
            {
                Vector3 bottom = e.sr.bounds.min;
                return bottom.y;
            }

            // 关键：家具图是「整图尺寸的透明 PNG」→ 图片底边 ≠ 家具的脚。
            // 必须用碰撞体算出来的「脚底那条边」当基准，否则所有家具排序值都一样，
            // 家具之间、家具和角色之间都不会正确遮挡。
            VillaObstacle ob = e.t.GetComponent<VillaObstacle>();
            if (ob != null && ob.corners != null && ob.corners.Length == 4)
            {
                // corners 约定：y 上→下（左上为原点），[2][3] 是靠近观众的前缘 = 家具的脚
                float vFront = 1f - ob.corners[2].y;   // 换算成「从下往上」的归一化高度
                Rect r = e.rt.rect;
                float localY = r.y + vFront * r.height;
                return e.rt.TransformPoint(new Vector3(0f, localY, 0f)).y;
            }

            // UI：取底边中点
            Vector3[] corners = new Vector3[4];
            e.rt.GetWorldCorners(corners);
            Vector3 bottomMid = (corners[0] + corners[3]) * 0.5f; // 左下 + 右下
            return bottomMid.y;
        }

        // ------------------------------------------------------------------
        // 应用
        // ------------------------------------------------------------------
        private void ApplyOrder()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];

                int order = CalcOrder(e);
                if (e.own != null) e.own.sortingOrder = order;
                else if (e.sr != null) e.sr.sortingOrder = order;
            }

            if (debugLog)
            {
                _debugTimer += Time.unscaledDeltaTime;
                if (_debugTimer >= debugInterval)
                {
                    _debugTimer = 0f;
                    Dump();
                }
            }
        }

        /// <summary>
        /// 把「脚底 Y」映射到「底图之上、HUD 之下」的排序值。
        /// 脚底越靠画面下方（离观众越近）→ 排序值越大 → 后画 → 盖住别人。
        /// </summary>
        private int CalcOrder(Entry e)
        {
            float h = Mathf.Max(1f, _backdropTopY - _backdropBottomY);
            float rel = Mathf.Clamp01((e.key - _backdropBottomY) / h);   // 0=画面最下，1=最上
            if (invert) rel = 1f - rel;

            int order = backdropOrder + 10 + Mathf.RoundToInt((1f - rel) * orderSpan);
            return Mathf.Clamp(order, backdropOrder + 1, -1);
        }

        /// <summary>把排序结果打到 Console，方便排查“家具挡不住人”。</summary>
        private void Dump()
        {
            var lines = new List<string>();
            lines.Add("[别墅排序] 参与排序 " + _entries.Count + " 个：");
            lines.Add("  底图: " + (_backdropRT != null ? _backdropRT.name + " (order=" + backdropOrder + ")" : "未找到"));
            lines.Add("  角色: " + (_characterUI != null ? "找到 " + _characterUI.name : "未找到"));

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                int order = CalcOrder(e);
                string applied = (e.own != null ? e.own.sortingOrder.ToString() : "无Canvas");
                string root = (e.own != null && e.own.rootCanvas != null ? e.own.rootCanvas.name : "?");

                VillaObstacle ob = e.t.GetComponent<VillaObstacle>();
                string obInfo = "无碰撞体";
                if (ob != null && ob.corners != null && ob.corners.Length == 4)
                {
                    obInfo = "脚边y=" + ob.corners[2].y.ToString("F3");
                }

                string sizeInfo = (e.rt != null
                    ? e.rt.rect.width.ToString("F0") + "x" + e.rt.rect.height.ToString("F0")
                    : "非UI");

                lines.Add(string.Format("  {0}  尺寸={1}  {2}  脚底Y={3:F1}  排序={4}(实际{5})  画布={6}",
                    e.t.name, sizeInfo, obInfo, e.key, order, applied, root));
            }

            Debug.Log(string.Join("\n", lines.ToArray()));
        }
    }
}
