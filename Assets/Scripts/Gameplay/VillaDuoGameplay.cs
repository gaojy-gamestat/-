using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    /// <summary>
    /// 双人布置玩法，运行时自动注入，不用改任何场景：
    ///
    ///   * 单人测试入口在主菜单「创建游戏」面板里（SampleSceneMenuRuntime，
    ///     点「单人测试（本地双人键位）」直接进第一关）；
    ///   * 进「第一关」→ 自动给 Oscar（WASD + E）和 Felix（方向键 + 右Shift）各建一个
    ///     背包面板（左/右两侧），点击垃圾袋选中 → 按布置键播放布置动画 →
    ///     动画结束后垃圾袋出现在别墅图上角色脚下（被角色压住），并从背包里消失；
    ///   * 走廊 / 卧室 / 客厅 / 厨房 4 个房间各放 1 个垃圾袋 → 本关完成。
    ///
    /// 布置动画由 <see cref="VillaCharacterWalker.PlayPlaceAnimation"/> 播放，
    /// 动画状态「布置」已合并进两人的行走控制器。
    /// </summary>
    [DisallowMultipleComponent]
    public class VillaDuoGameplay : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // 自动注入
        // ------------------------------------------------------------------
        private static bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_hooked) return;
            _hooked = true;

            // 静音「A Native Collection has not been disposed」提示：
            // 该泄漏来自联机包（Netcode/Transport）间接引入的 Unity.Collections 内部，
            // 每次 Play 退出报一次、无害。关掉全局泄漏检测不影响任何功能。
            Unity.Collections.NativeLeakDetection.Mode = Unity.Collections.NativeLeakDetectionMode.Disabled;

            SceneManager.sceneLoaded += OnSceneLoaded;
            HandleScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            HandleScene(scene);
        }

        private static void HandleScene(Scene scene)
        {
            // 单人测试入口已移到主菜单「创建游戏」面板（SampleSceneMenuRuntime），
            // 这里只负责第一关的背包 / 布置玩法。
            if (scene.name == "第一关")
            {
                EnsureGameplay();
                EnsureCatchSystem();
            }
        }

        private static void EnsureGameplay()
        {
            if (FindObjectOfType<VillaDuoGameplay>() != null) return;
            new GameObject("双人布置玩法", typeof(VillaDuoGameplay));
        }

        /// <summary>
        /// 「被发现」判定组件（Level01DukeGameplay）没有挂在场景里的 Duke 物体上，
        /// 这里运行时自动补挂——不补挂的话同房间检测永远不会执行。
        /// </summary>
        private static void EnsureCatchSystem()
        {
            DukePatroller duke = FindObjectOfType<DukePatroller>();
            if (duke == null)
            {
                Debug.LogWarning("[双人布置玩法] 场景里没有 DukePatroller，无法启用被发现判定。");
                return;
            }
            if (duke.GetComponent<Level01DukeGameplay>() != null) return;
            duke.gameObject.AddComponent<Level01DukeGameplay>();
            Debug.Log("[双人布置玩法] 已自动挂上被发现判定（Level01DukeGameplay）到 Duke。");
        }

        /// <summary>优先用场景里已有的中文字体，找不到就用系统微软雅黑。</summary>
        private static Font LoadUiFont()
        {
            Text existing = FindObjectOfType<Text>();
            if (existing != null && existing.font != null && existing.font.name != "Arial")
            {
                return existing.font;
            }
            return Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // ------------------------------------------------------------------
        // 第一关：背包 + 布置
        // ------------------------------------------------------------------
        /// <summary>本关唯一道具：垃圾袋（Assets/Resources/道具栏/道具_垃圾袋.png）。</summary>
        private const string GarbageBag = "道具栏/道具_垃圾袋";
        /// <summary>通关要求：这 4 个房间各放 1 个垃圾袋。</summary>
        private static readonly string[] TargetRooms = { "走廊", "卧室", "客厅", "厨房" };
        /// <summary>每人初始垃圾袋数（两人共 4 个，正好对应 4 个房间）。</summary>
        public int bagsPerPlayer = 2;

        /// <summary>道具显示高度 = 别墅图高度的百分之几。</summary>
        [Range(0.02f, 0.2f)] public float propHeightRatioOfVilla = 0.07f;
        /// <summary>背包面板高度占屏幕高度的比例。</summary>
        [Range(0.2f, 0.8f)] public float bagHeightRatioOfScreen = 0.5f;

        private sealed class PlayerBag
        {
            public VillaCharacterWalker walker;
            public RectTransform panel;
            public Canvas canvas;
            public readonly List<Image> cells = new List<Image>();
            public readonly List<string> items = new List<string>();
            public int selectedIndex = -1;
            public Text hintText;
        }

        private readonly List<PlayerBag> _bags = new List<PlayerBag>();
        private readonly HashSet<string> _roomsWithBag = new HashSet<string>();
        private bool _cleared;
        private bool _hintedRestart;

        private void Start()
        {
            StartCoroutine(SetupBags());
        }

        private IEnumerator SetupBags()
        {
            // 等角色初始化完（同帧加载时 VillaImage 可能还没就绪）
            for (int i = 0; i < 30 && _bags.Count == 0; i++)
            {
                yield return null;
            }

            foreach (VillaCharacterWalker w in FindObjectsOfType<VillaCharacterWalker>())
            {
                if (w.controlScheme == VillaCharacterWalker.ControlScheme.Both) continue;

                PlayerBag bag = new PlayerBag { walker = w };
                bool isOscar = w.controlScheme == VillaCharacterWalker.ControlScheme.Oscar;

                // 初始道具：两个玩家各 2 个垃圾袋（共 4 个，正好对应 4 个目标房间）
                for (int i = 0; i < bagsPerPlayer; i++)
                {
                    bag.items.Add(GarbageBag);
                }

                BuildBagPanel(bag, isOscar);
                RefreshBag(bag);
                _bags.Add(bag);
            }

            EnsureEventSystem();
            Debug.Log($"[双人布置玩法] 背包就绪，玩家数 = {_bags.Count}，每人 {bagsPerPlayer} 个垃圾袋");
        }

        /// <summary>建一个背包面板（Oscar 在左、Felix 在右），5 个格子，点击选中。</summary>
        private void BuildBagPanel(PlayerBag bag, bool isOscar)
        {
            string bagRes = isOscar ? "道具栏/背包_Oscar" : "道具栏/背包_Felix";
            Sprite bagSprite = Resources.Load<Sprite>(bagRes);
            if (bagSprite == null)
            {
                Debug.LogWarning($"[双人布置玩法] 找不到背包图 {bagRes}，面板用纯色底代替。");
            }

            GameObject panelGo = new GameObject(isOscar ? "背包_Oscar" : "背包_Felix",
                typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = panelGo.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 300;
            bag.canvas = canvas;
            panelGo.transform.SetParent(bag.walker.VillaCanvas != null
                ? bag.walker.VillaCanvas.transform
                : FindObjectOfType<Canvas>()?.transform, false);

            RectTransform rt = panelGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(isOscar ? 0f : 1f, 0.5f);
            rt.anchorMax = rt.anchorMin;
            rt.pivot = new Vector2(isOscar ? 0f : 1f, 0.5f);
            rt.anchoredPosition = new Vector2(isOscar ? 12f : -12f, 0f);

            float h = Screen.height * bagHeightRatioOfScreen;
            float ratio = bagSprite != null ? bagSprite.rect.width / bagSprite.rect.height : 0.32f;
            rt.sizeDelta = new Vector2(h * ratio, h);

            Image bg = panelGo.AddComponent<Image>();
            bg.sprite = bagSprite;
            bg.color = bagSprite != null
                ? Color.white
                : new Color(0.1f, 0.1f, 0.15f, 0.85f);
            bg.raycastTarget = false;

            // 5 个格子竖向均分
            float cellSize = rt.rect.width * 0.72f;
            float step = h / 6f;
            for (int i = 0; i < 5; i++)
            {
                GameObject cellGo = new GameObject($"格子{i}", typeof(Image));
                cellGo.transform.SetParent(rt, false);
                Image cell = cellGo.GetComponent<Image>();
                cell.color = new Color(1f, 1f, 1f, 0.14f);
                cell.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                cell.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                cell.rectTransform.sizeDelta = new Vector2(cellSize, cellSize);
                cell.rectTransform.anchoredPosition = new Vector2(0f, h * 0.5f - step * (i + 1));
                bag.cells.Add(cell);
            }

            // 底部操作提示
            GameObject hintGo = new GameObject("提示", typeof(Text));
            hintGo.transform.SetParent(rt, false);
            Text hint = hintGo.GetComponent<Text>();
            hint.font = LoadUiFont();
            hint.fontSize = Mathf.Max(14, Mathf.RoundToInt(h * 0.035f));
            hint.alignment = TextAnchor.MiddleCenter;
            hint.color = Color.yellow;
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.raycastTarget = false;
            hint.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            hint.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            hint.rectTransform.sizeDelta = new Vector2(h * ratio * 1.6f, step);
            hint.rectTransform.anchoredPosition = new Vector2(0f, -step * 0.6f);
            hint.text = isOscar
                ? "Oscar：点击垃圾袋选中\nWASD 移动 / E 布置"
                : "Felix：点击垃圾袋选中\n方向键移动 / 右Shift 布置";
            bag.hintText = hint;
            bag.panel = rt;
        }

        private void SelectItem(PlayerBag bag, int index)
        {
            if (index >= bag.items.Count) return;
            bag.selectedIndex = bag.selectedIndex == index ? -1 : index;
            RefreshBag(bag);
        }

        private void RefreshBag(PlayerBag bag)
        {
            for (int i = 0; i < bag.cells.Count; i++)
            {
                Image cell = bag.cells[i];
                bool selected = i == bag.selectedIndex;
                cell.color = selected
                    ? new Color(1f, 0.85f, 0.25f, 0.55f)
                    : new Color(1f, 1f, 1f, 0.14f);

                // 清掉旧道具图标
                Transform child = cell.transform.childCount > 0 ? cell.transform.GetChild(0) : null;
                if (child != null && (i >= bag.items.Count || bag.items[i] != child.name))
                {
                    Destroy(child.gameObject);
                    child = null;
                }

                if (i < bag.items.Count && child == null)
                {
                    Sprite s = Resources.Load<Sprite>(bag.items[i]);
                    if (s != null)
                    {
                        GameObject iconGo = new GameObject(bag.items[i], typeof(Image));
                        iconGo.transform.SetParent(cell.transform, false);
                        Image icon = iconGo.GetComponent<Image>();
                        icon.sprite = s;
                        icon.preserveAspect = true;
                        icon.raycastTarget = false;
                        icon.rectTransform.anchorMin = Vector2.zero;
                        icon.rectTransform.anchorMax = Vector2.one;
                        // 图标约占格子的 70%，别撑出背包图上画的方框
                        float inset = cell.rectTransform.rect.height * 0.15f;
                        icon.rectTransform.offsetMin = Vector2.one * inset;
                        icon.rectTransform.offsetMax = -Vector2.one * inset;
                    }
                }

                if (i >= bag.items.Count && child != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }

        private void Update()
        {
            // READY/GO 开场字幕或结算面板冻结期间（timeScale=0）不响应任何操作
            if (Time.timeScale <= 0f) return;

            // 垃圾袋放完但没凑齐 4 个房间 → 背包面板上提示按 R 重来
            if (!_cleared && _bags.Count > 0 && AllBagsUsed())
            {
                if (!_hintedRestart)
                {
                    _hintedRestart = true;
                    for (int i = 0; i < _bags.Count; i++)
                    {
                        if (_bags[i].hintText != null)
                        {
                            _bags[i].hintText.text = "垃圾袋放完了但没凑齐 4 个房间\n按 R 重来";
                        }
                    }
                }

                if (Input.GetKeyDown(KeyCode.R))
                {
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                    return;
                }
            }

            if (_cleared) return;

            // 点击选中：直接做屏幕命中测试，不依赖 EventSystem 派发
            if (Input.GetMouseButtonDown(0))
            {
                HandleBagClicks();
            }

            for (int i = 0; i < _bags.Count; i++)
            {
                PlayerBag bag = _bags[i];
                VillaCharacterWalker w = bag.walker;
                if (w == null || bag.items.Count == 0 || bag.selectedIndex < 0) continue;
                if (w.IsPlacing) continue;
                if (!Input.GetKeyDown(w.PlaceKey)) continue;

                w.PlayPlaceAnimation();
                StartCoroutine(PlaceItemAfterAnimation(bag));
            }
        }

        private void HandleBagClicks()
        {
            Vector2 mouse = Input.mousePosition;
            for (int i = 0; i < _bags.Count; i++)
            {
                PlayerBag bag = _bags[i];
                Camera cam = bag.canvas != null && bag.canvas.renderMode == RenderMode.ScreenSpaceCamera
                    ? bag.canvas.worldCamera
                    : null;

                for (int k = 0; k < bag.cells.Count; k++)
                {
                    Image cell = bag.cells[k];
                    if (cell == null) continue;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(cell.rectTransform, mouse, cam))
                    {
                        continue;
                    }

                    SelectItem(bag, k);
                    Debug.Log($"[双人布置玩法] 点击 {bag.walker.gameObject.name} 的格子{k}（{bag.items.Count} 件道具），selectedIndex = {bag.selectedIndex}");
                    break;
                }
            }
        }

        private IEnumerator PlaceItemAfterAnimation(PlayerBag bag)
        {
            VillaCharacterWalker w = bag.walker;
            yield return new WaitForSeconds(w.placeDuration);
            if (w == null || bag.selectedIndex < 0 || bag.selectedIndex >= bag.items.Count)
            {
                yield break;
            }

            string res = bag.items[bag.selectedIndex];
            int index = bag.selectedIndex;

            // 布置动画结束时角色位置就是道具落点（布置期间移动已锁定）
            Image prop = SpawnProp(w, res);
            if (prop != null)
            {
                Debug.Log($"[双人布置玩法] {w.gameObject.name} 布置了 {res}（别墅图位置 {w.ImageNorm}）");
            }

            bag.items.RemoveAt(index);
            bag.selectedIndex = -1;
            RefreshBag(bag);

            // 道具必须压在角色之下：先排到别墅子树最底，再把两个角色镜像提到最前
            prop?.transform.SetAsFirstSibling();
            foreach (PlayerBag b in _bags)
            {
                if (b.walker != null) b.walker.BringMirrorToFront();
            }

            RegisterPlacement(w);
        }

        private bool AllBagsUsed()
        {
            for (int i = 0; i < _bags.Count; i++)
            {
                if (_bags[i].items.Count > 0) return false;
            }
            return true;
        }

        /// <summary>登记这次布置落在哪个房间，凑齐 4 个房间即通关。</summary>
        private void RegisterPlacement(VillaCharacterWalker w)
        {
            string roomName = RoomAt(w);
            if (string.IsNullOrEmpty(roomName))
            {
                Debug.Log("[双人布置玩法] 这里不在走廊/卧室/客厅/厨房，垃圾袋不计入通关。");
                return;
            }

            if (!_roomsWithBag.Add(roomName))
            {
                Debug.Log($"[双人布置玩法] {roomName} 已经有垃圾袋了，这一个重复，不计入通关。");
                return;
            }

            if (_roomsWithBag.Count >= TargetRooms.Length && !_cleared)
            {
                _cleared = true;
                ShowClearBanner();
                Debug.Log("[双人布置玩法] 通关：走廊、卧室、客厅、厨房各 1 个垃圾袋。");
            }
        }

        /// <summary>角色当前站的是哪块地板 → 归类成 4 个目标房间之一。</summary>
        private string RoomAt(VillaCharacterWalker w)
        {
            VillaRoomBuilder room = w.Room;
            if (room == null || !room.IsReady) return null;

            IList<VillaFloorArea> floors = room.Floors;
            for (int i = 0; i < floors.Count; i++)
            {
                VillaFloorArea f = floors[i];
                if (f == null || !f.enabled || !f.gameObject.activeInHierarchy) continue;
                if (!f.ContainsImageNorm(w.ImageNorm)) continue;
                return ClassifyRoom(f.gameObject.name);
            }
            return null;
        }

        private static string ClassifyRoom(string floorName)
        {
            if (floorName.Contains("走廊")) return "走廊";
            if (floorName.Contains("客厅")) return "客厅";
            if (floorName.Contains("厨房")) return "厨房";
            if (floorName.Contains("主卧") || floorName.Contains("卧室")) return "卧室";
            return null;
        }

        // ------------------------------------------------------------------
        // 通关提示
        // ------------------------------------------------------------------
        private void ShowClearBanner()
        {
            GameObject go = new GameObject("通关提示", typeof(Canvas), typeof(GraphicRaycaster), typeof(Text));
            Canvas c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 600;
            Text t = go.GetComponent<Text>();
            t.font = LoadUiFont();
            t.fontSize = 46;
            t.color = new Color(1f, 0.9f, 0.3f);
            t.alignment = TextAnchor.MiddleCenter;
            t.text = "本关完成！\n杜克家的垃圾已全部还回去";
            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1000f, 220f);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// 在别墅图上、角色当前所处位置生成道具图标。
        /// 落点换算和角色镜像同一套：ImageNorm → 世界坐标 → 屏幕 → 别墅图局部坐标。
        /// </summary>
        private Image SpawnProp(VillaCharacterWalker w, string propResource)
        {
            Sprite sprite = Resources.Load<Sprite>(propResource);
            if (sprite == null)
            {
                Debug.LogWarning($"[双人布置玩法] 道具图加载失败：{propResource}");
                return null;
            }

            Image villaImage = w.VillaImage;
            Canvas canvas = w.VillaCanvas;
            Camera cam = w.ViewCamera;
            if (villaImage == null || canvas == null)
            {
                Debug.LogWarning("[双人布置玩法] 别墅图 / Canvas 未就绪，道具没放出来。");
                return null;
            }

            VillaRoomBuilder room = w.Room;
            Vector3 world;
            if (room != null && room.IsReady)
            {
                world = room.ImageNormToGround(w.ImageNorm);
            }
            else
            {
                world = w.transform.position;
            }

            Vector3 screen = cam != null
                ? cam.WorldToScreenPoint(world)
                : new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);

            RectTransform villaRt = villaImage.rectTransform;
            Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    villaRt, screen, uiCam, out Vector2 local))
            {
                local = Vector2.zero;
            }

            // 名字以「道具_」开头 → VillaDepthSorter 会把它强制排在角色之下
            string shortName = propResource.Substring(propResource.LastIndexOf('/') + 1);
            GameObject go = new GameObject(shortName, typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(villaRt, false);
            rt.SetAsFirstSibling();            // 没有排序器时也压在角色镜像之下
            rt.localPosition = local + Vector2.up * villaRt.rect.height * 0.015f;
            float h = villaRt.rect.height * propHeightRatioOfVilla;
            rt.sizeDelta = new Vector2(h * sprite.rect.width / sprite.rect.height, h);
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }
    }
}
