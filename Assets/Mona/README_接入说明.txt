Mona 新版紫衣四向行走

将 Assets/Mona/Mona.prefab 拖入临时场景，点击播放即可预览默认的 WalkFront。Prefab 已配置 SpriteRenderer 和 Animator，引用 Mona.controller。
程序可调用 animator.Play("WalkFront", 0, 0f)，其余方向使用 WalkBack、WalkRight、WalkLeft。建议只在动作切换时调用。
WalkFront 面向镜头，WalkBack 背向镜头，WalkRight 和 WalkLeft 分别朝屏幕右、左。四个动作原地循环，位移由玩法控制。
每向 12 帧，单帧 100 毫秒，整轮 1.2 秒。1024×1024 透明 PNG，PPU 1024，Full Rect，Bilinear，无压缩。脚底支点采用源图左上原点 (512,948)，对应 Unity 归一化 (0.5,0.07421875)。
素材版本为桌面「Mona新版四向行走_发缘修正版」，包含正面 07、背面 04、背面 07 发缘修订。