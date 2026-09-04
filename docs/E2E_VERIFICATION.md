# E2E 双实例联机验证证据

## 最新一轮（UI 驱动，2026-09-04）

- 构建结果：`[Build] result=Succeeded size=116393071 errors=0`（含 TMP Essential Resources + Noto Sans SC 中文字体）
- 编辑器批处理校验：`BatchValidate` 37/37 全部通过（场景/Prefab/NetworkManager/UI 接线）
- 验证方式：团结 6000.4.0a2 alpha 编辑器（免许可证）构建的 Windows Player 双进程；Host/Client 全程通过 **按钮回调**（`Btn_StartHost.onClick.Invoke()` / `Btn_JoinClient.onClick.Invoke()` / `Btn_StartGame.onClick.Invoke()`，与真人点击同代码路径）驱动；DirectIP 模式（仓库默认 Relay，见下文）
- 中文字体渲染已在窗口化实例中目视确认（创建房间 / 房间码：127.0.0.1 / 1/2 玩家 等全部正常）

### Host 侧证据（host_evidence.log）
```
HOST_UI_CLICKED 已点击 创建游戏/创建房间 按钮
HOST_READY host 已启动，等待 Client 加入
READY_2_OF_2 满员
HOST_STARTGAME 满员后开始游戏（点击 开始游戏 按钮 → Host 调用 NetworkSceneManager.LoadScene）
HOST_UI_CLICKED 已点击 开始游戏 按钮
HOST_SPAWN_COUNT 玩家数量=2（期望 2）
HOST_SYNC_SAMPLE Host 玩家最终 pos=(0.00, 0.08, 4.30) rot=120.0
SAVE_TEST 开始 Host 存档验证
HOST_SAVE_OK Host 保存成功
HOST_LOAD_OK Host 读取成功：saveName=E2E自动存档, items=2, neighborStates=1
HOST_LIST_OK 存档列表包含 e2e_test
HOST_E2E_DONE
```

### Client 侧证据（client_evidence.log）
```
CLIENT_UI_CLICKED 已点击 加入游戏/加入 按钮（房间码=127.0.0.1）
GAMEPLAY_SYNCED 网络场景已同步：GamePlay
SAVE_PROTECTED Client 写存档已被拦截
SAVE_LIST_PROTECTED Client 读取存档列表已被拦截（返回空）
POSITION_SYNC_VERIFIED 远端玩家位置从 (0.00, 0.08, 4.04) 变化到 (0.00, 0.08, 4.17)
ROTATION_SYNC_VERIFIED 远端玩家旋转从基准 0.0° 变化到 15.0°
CLIENT_E2E_ALL_PASS
```

### 断开处理证据
```
[Net][Host] Client 1 断开，剩余 1/2
[Net][Host] Client 0 断开，剩余 0/2
```

## 覆盖项对照

| 要求 | 证据 |
|---|---|
| Compilation | Player 构建成功 0 errors；BatchValidate 编译通过 |
| MainMenu Scene Load | BatchValidate PASS |
| Host Create Room | HOST_UI_CLICKED + HOST_READY |
| Join Code Generate | Relay 链路已实现；DirectIP 模式房间码显示 127.0.0.1（窗口化截图目视确认） |
| Client Join | CLIENT_UI_CLICKED + READY_2_OF_2 |
| 1/2 → 2/2 | Host 侧 STATE 状态机日志（WaitingForPlayer → ReadyToStart） |
| Host Start | HOST_UI_CLICKED 开始游戏按钮 |
| GamePlay Network Scene Load | GAMEPLAY_SYNCED（Client 端网络场景同步） |
| Host/Client Spawn | HOST_SPAWN_COUNT = 2 |
| Position Sync | POSITION_SYNC_VERIFIED |
| Rotation Sync | ROTATION_SYNC_VERIFIED |
| Disconnect | Client 退出后 Host 断开回调日志；Client 断线时收到"与 Host 的连接已断开" |
| Client Save Protection | SAVE_PROTECTED / SAVE_LIST_PROTECTED |
| Host Save / Load / List | HOST_SAVE_OK / HOST_LOAD_OK / HOST_LIST_OK |

## 关于 Relay 的说明

代码默认走 Relay（`CreateAllocationAsync → GetJoinCodeAsync → RelayServerData(allocation,"dtls") → StartHost`；Client `JoinAllocationAsync → StartClient`）。
本验证环境未绑定 UGS 项目（需要用户 Unity 账号在编辑器里一次性绑定），因此 E2E 使用 `ConnectMode.DirectIP` 走 NGP + Unity Transport 直连，
除"Relay 服务器分配"这一步外，其余链路（Authentication 初始化、NGO 连接、场景同步、对象同步）完全一致。
