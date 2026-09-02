# 双人潜入恶作剧游戏（Neighbours from Hell-like）

双人合作潜入整蛊游戏。本仓库为主仓库，当前里程碑：**Unity Netcode for GameObjects + Unity Relay + UGS 联机底座全链路打通**（MainMenu → 创建/加入房间 → Relay → NGO → 双人同步 → GamePlay）。

## 技术栈

- 团结引擎 / Unity 2022.3.62（中国版，ProjectVersion: 2022.3.62f3c1）
- Netcode for GameObjects **1.15.1**（2022.3 官方支持线；NGO 2.x 需要 Unity 6）
- Unity Transport **1.5.0**
- Unity Relay **1.2.0**（`RelayServerData(Allocation/JoinAllocation, "dtls")` 官方构造器；本版本 SDK 不含 AllocationUtils）
- Unity Authentication **3.7.4**（匿名登录）+ Unity Services Core **1.18.0**
- URP 未使用（Built-in 管线）+ TextMeshPro 3.0.7

## 场景与工程结构

```
Assets/
  Scenes/MainMenu.unity   主菜单（基于原 SampleScene 的 UI，保留 创建游戏/加入游戏/设置/退出/制作人员）
  Scenes/GamePlay.unity   联机游戏场景（占位房间，可正常被 NGO 网络场景管理加载）
  Scenes/SampleScene.unity 原始场景（保留未动）
  Prefabs/Player.prefab   最小联网玩家占位（NetworkObject + NetworkTransform + CharacterController，胶囊体占位模型）
  Prefabs/NetworkPrefabsList.asset
  Scripts/
    Net/GameNetworkManager.cs     Relay 建房/加入、房间状态机、玩家生成策略、连接审批
    UI/MainMenuUIController.cs    主菜单 UI 控制（Panel_CreateRoom / Panel_JoinRoom）
    Player/PlayerController.cs    Host-authoritative 移动（Owner 采集输入 → ServerRpc → Host 执行）
    Player/PlayerIdentity.cs      机敏哥(Host) / 老实人(Client) 身份标识
    Save/GameSaveData.cs          JsonUtility 可序列化存档结构（无 Dictionary，全部 [Serializable] 列表）
    Save/SaveSystem.cs            Host-only 存档（Client 读写被硬性拦截并记日志）
    Core/GameplayBootstrap.cs     GamePlay 场景验证日志
    Editor/ProjectSetup.cs        一次性工程落地脚本（batchmode -executeMethod）
    Editor/BatchValidate.cs       批处理工程校验 / 构建测试客户端 / 生成 TMP 中文字体
    Editor/E2EHostTest.cs         编辑器侧 E2E Host 测试
    Core/E2EClientBootstrap.cs    E2E Client 引导（--e2e-client 命令行参数触发）
    Core/E2EHostBootstrap.cs      E2E Host 引导（--e2e-host 命令行参数触发）
```

## 主菜单联机链路

1. 点击 **创建游戏** → `Panel_CreateRoom`：匿名登录 Authentication → 创建 Relay Allocation → 获取 Join Code 显示到 `Txt_RoomCode`，状态显示 `1/2 玩家 / 等待玩家加入`
2. 点击 **加入游戏** → `Panel_JoinRoom`：输入 Join Code → 加入 Relay → StartClient
3. Client 加入后 Host 显示 `2/2 玩家 / 可以开始游戏`，`Btn_StartHost` 解锁（未满员不可开始）
4. Host 点击 **开始游戏** → `NetworkManager.SceneManager.LoadScene("GamePlay")`，Client 禁止自行 `SceneManager.LoadScene`，跟随网络场景同步
5. 进入 GamePlay 后 Host 统一为双方生成 Player NetworkObject（Host 在 (0,1.2,4)，Client 在 (0,1.2,-4)），位置/旋转经 NetworkTransform（Server Authority）同步

房间状态机：`Idle → Creating → WaitingForPlayer → PlayerJoined → ReadyToStart → Starting → LoadingGame（Error 任意状态可进入）`

## 首次打开前必须完成的两件事

1. **绑定 UGS 项目**：用 Unity/团结 Hub 登录后，在编辑器 `Edit > Project Settings > Services` 绑定你的 UGS 项目并启用 Authentication 与 Relay。未绑定时走 Relay 会报错（代码会给出明确错误提示）；本机联调可在 `NetworkManager_GO > GameNetworkManager > ConnectMode` 切换为 `DirectIP`（默认 127.0.0.1:7777）。
2. **TMP 字体**：菜单 `Tools > 联机工程 > 生成 TMP 中文字体设置`（一次性，生成微软雅黑动态字体资产，保证中文 UI 正常渲染）。

## 工程校验与 E2E 测试

- 批处理校验：`Tuanjie/Unity -batchmode -quit -projectPath . -executeMethod GameNet.EditorTools.BatchValidate.Validate`
- 构建 E2E 客户端：`-executeMethod GameNet.EditorTools.BatchValidate.BuildWindowsPlayer`
- 双实例 E2E：`E2EClient.exe -batchmode --e2e-host` 与 `E2EClient.exe -batchmode --e2e-client`
- 最近一次验证证据：[docs/E2E_VERIFICATION.md](docs/E2E_VERIFICATION.md)

## 角色（后续建模占位说明）

- **机敏哥**（Host）：188cm 高瘦、长脸大耳略长鼻、B 方案脸
- **老实人**（Client）：168cm 矮胖、圆脸敦厚

当前 `Player.prefab` 使用胶囊体占位（Host 蓝 / Client 橙，由 `PlayerIdentity` 运行时着色）；替换正式模型时保持 `NetworkObject + NetworkTransform + CharacterController` 挂在根节点、视觉放在 `Visual` 子物体即可。
