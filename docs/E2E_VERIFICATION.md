# E2E 双实例联机验证证据

- 日期：2026-09-03（验证副本：Tuanjie 6000.4.0a2 alpha 编辑器构建的 Windows Player，双进程 DirectIP 模式）
- 构建结果：[Build] result=Succeeded size=96801199 errors=0
- 编辑器批处理校验：BatchValidate 37/37 全部通过（场景/Prefab/NetworkManager/UI 接线）

## Host 侧证据（host_evidence.log）
```
03:38:36.424 HOST_BOOT 进入运行时
03:38:55.808 READY_2_OF_2 满员
03:38:56.785 HOST_STARTGAME 满员后开始游戏（Host 调用 NetworkSceneManager.LoadScene）
03:38:57.285 HOST_SPAWN 玩家对象 ClientId=1 pos=(0.00, 1.20, -4.00)
03:38:57.286 HOST_SPAWN 玩家对象 ClientId=0 pos=(0.00, 0.33, 4.00)
03:38:57.286 HOST_SPAWN_COUNT 玩家数量=2（期望 2）
03:38:57.800 HOST_MOVED Host 玩家移动到 (0.00, 0.08, 4.00)
03:39:01.061 HOST_ROTATED Host 玩家旋转到 31.2°
03:39:01.801 HOST_SYNC_SAMPLE Host 玩家最终 pos=(0.00, 0.08, 4.27) rot=120.0
03:39:03.801 SAVE_TEST 开始 Host 存档验证
03:39:03.806 HOST_SAVE_OK Host 保存成功
03:39:03.807 HOST_LOAD_OK Host 读取成功：saveName=E2E自动存档, items=2, neighborStates=1
03:39:03.817 HOST_LIST_OK 存档列表包含 e2e_test
03:39:04.805 HOST_E2E_DONE
```

## Client 侧证据（client_evidence.log）
```
03:38:54.509 CLIENT_BOOT
03:38:56.914 GAMEPLAY_SYNCED 网络场景已同步：GamePlay
03:38:56.927 SAVE_PROTECTED Client 写存档已被拦截
03:38:56.927 SAVE_LIST_PROTECTED Client 读取存档列表已被拦截（返回空）
03:38:59.864 POSITION_SYNC_VERIFIED 远端玩家位置从 (0.00, 0.08, 4.02) 变化到 (0.00, 0.08, 4.13)
03:39:01.032 ROTATION_SYNC_VERIFIED 远端玩家旋转从基准 0.0° 变化到 15.0°
03:39:01.032 CLIENT_E2E_ALL_PASS
```

## 断开处理证据
```
[Net][Host] Client 1 断开，剩余 1/2
[Net][Host] Client 0 断开，剩余 0/2
```
