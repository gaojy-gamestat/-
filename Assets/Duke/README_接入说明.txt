# Duke 三个待机与四向行走动作接入说明

当前版本：2026-11-04，保留三个待机和已接入的前后走路，新增指定来源的向右、向左行走。
适用：团结引擎 / Unity 2022.3.62f3c1；Built-in渲染管线。

## 文件与播放

将 Assets/Duke/Duke.prefab 拖到场景。它已配置 SpriteRenderer 和 Animator，引用 Duke.controller；默认循环播放 WatchTV。
动画绑定同一物体的 SpriteRenderer.m_Sprite。保持 Assets/Duke 的 PNG、动画、Controller、Prefab 与 .meta 一起提交，不能只上传Prefab。

```csharp
Animator animator = duke.GetComponent<Animator>();
animator.Play("WatchTV", 0, 0f); // 看电视
animator.Play("Eat", 0, 0f);     // 进食
animator.Play("Sleep", 0, 0f);   // 睡觉
animator.Play("WalkFront", 0, 0f); // 正面朝向观众，原地行走
animator.Play("WalkBack", 0, 0f);  // 背对观众，原地行走
animator.Play("WalkRight", 0, 0f); // 向右走
animator.Play("WalkLeft", 0, 0f);  // 向左走
```

只在动作改变时调用，不要每帧从0重播。Animator.speed保持1。七个状态位于Base Layer，无自动过渡、无参数和自定义运行时脚本。

## 素材规格与时序

| 状态 | PNG数量 | 播放段数 | 循环时长 | 左下原点归一化支点 |
|---|---:|---:|---:|---|
| WatchTV | 12 | 12 | 5秒 | (0.54980469, 0.32031250) |
| Eat | 16 | 16 | 4秒 | (0.60546875, 0.07714844) |
| Sleep | 12 | 24 | 4.8秒 | (0.5, 0.47265625) |
| WalkFront | 12 | 12 | 1.2秒 | (0.5, 0.07421875) |
| WalkBack | 12 | 12 | 1.2秒 | (0.5, 0.07421875) |
| WalkRight | 12 | 12 | 1.2秒 | (0.5, 0.07421875) |
| WalkLeft | 12 | 12 | 1.2秒 | (0.5, 0.07421875) |

共88张1024×1024 RGBA原图，PPU=1024，Full Rect、Bilinear、无MipMap、无压缩；原图未改写。
七个动画均开启Loop Time。Samples=20是50毫秒时间网格，不代表每张图只停50毫秒。
睡觉01–06重复三轮，再接07–12。最后一帧的保持关键帧位于总时长减50ms处，以包含Unity自动保持一个采样格的时间。
来源时序和参考摆位记录位于SourceMetadata。

## 第一关接入与尺寸

当前第一关房子是Overlay UI。标准SpriteRenderer直接拖进去不会自动适配房间和UI遮挡；需由程序对接现有UI镜像显示。
复用UI Image时，显示当前SpriteRenderer.sprite，preserveAspect=true、raycastTarget=false；按动作设置characterAnchorInFrame，不能假定Image会读取Sprite pivot。
看电视按座位/骨盆对齐，睡觉按床中央对齐，进食按脚底对齐；需要行为系统设置对应交互点。现有freezeAnimationWhenIdle会冻结动作，不适用于循环待机。
PNG不含房间家具。看电视的台灯前景、睡觉的床及其他遮挡由关卡处理；placement.json只是原底图参考，不是Unity世界坐标。

## 四向行走与交付边界

前后素材沿用已经接入的04 向前、05 向后；本次新增只来自用户指定duck基本动画/06 向右与07 向左，各12张PNG，每帧100毫秒，1.2秒循环。原图字节保留，不做镜像替代或重画。
四方向均使用来源支点左上(512,948)，转换Unity归一化(0.5,0.07421875)，PPU1024、Full Rect、Bilinear、无MipMap和无压缩。左右是独立图片，保持flipX=false。
Controller现有WatchTV/Eat/Sleep/WalkFront/WalkBack不变，新增WalkRight/WalkLeft；默认仍WatchTV，无自动过渡。原Prefab及Controller GUID保留，已有场景引用无需重新连接。
只在动作改变时调用Animator.Play，不要每帧从0重播。方向由角色实际移动或行为逻辑决定。停下后的动作由程序选择；本包三个待机分别是看电视、进食、睡觉，并非四方向站立待机。
移动位置、路线、地面碰撞、UI镜像和联机由程序对接。原第一关的旧方向脚本不会因新增状态而自动支持Duke，须显式使用本Controller状态名。
动画只修改SpriteRenderer.m_Sprite，不包含Transform位置曲线或Root Motion；不增加导航或玩法脚本，不改变正式关卡。
旧版通用Walk状态不恢复；后续统一调用明确的四方向名称。
