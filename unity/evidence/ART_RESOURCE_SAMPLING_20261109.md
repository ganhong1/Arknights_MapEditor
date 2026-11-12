# Unity 美术资源分层抽样 · 2026-11-09

## 环境与范围

实际运行 Unity **2021.3.39f1**、Windows、D3D11、Built-in Render Pipeline，显卡 RTX 4060 Ti。单独的批处理测试工程使用 GPU 渲染并保存 PNG；没有将浏览器画面或预先导出的截图作为 Unity 渲染结果。

主要样本来自 PC 内置资源的 5-10 场景、龙门中心地图资源、absin 特效、公共 UI 与地块包，补充电塔和出怪点模型动画。测试覆盖基础对象、组合 Prefab、完整场景、随时间变化的效果以及卸载。它是类型分层抽样，不是全部游戏资产的兼容率统计。

主插件代码、游戏、实验私服均未操作。测试工程、转换副本、缓存和图片放在 D 盘。当前关卡工作台的文档、草稿、历史与恢复标识分别在测试前后取只读快照。

本机证据根：`D:/Temporary_Embedded_Project/storage/map-editor-unity/art-probe-20261109`。下文路径均相对此根。

## 加载方法与来源

1. 原始 `bg_lmc.ab` 直接交给 `AssetBundle.LoadFromFile`，失败信息明确包括不支持压缩格式 4。
2. 用已存在的 `@arkntools/unity-js 5.3.0` 读取方舟压缩格式，将副本写成标准 UnityFS；包内节点逐字节核对，Unity 版本与平台标签不改，原文件不改。
3. 按样本依赖加载资源包，再分别调用 Unity 原生的 AssetBundle、Renderer、ParticleSystem、Animator、AnimationClip 和 SceneManager 接口。
4. 不给原材质替换 Shader。少量诊断显示使用专门材质或相机，报告逐项记录此操作。
5. 关闭测试场景后卸载本轮加载的 Bundle。独立 Unity 进程自动退出。

初始 20 包、补充地块 1 包和动画 2 包共 **23 个当前 PC 原包转换副本**；另复用 2 个历史转换依赖，总计最多 **25 包**。历史依赖的 CAB、Unity 版本、平台和文件 SHA 已核对，未将其称为当前原包：

- `historical/anon/88b3c3ecaaefa6003de730686dd5ac53.bin`：MonoScript 元数据。它不是 Torappu 脚本的可执行实现。
- `historical/anon/dfba18b16546da2b44cbbe0fa4019066.bin`：Shader、材质和少量纹理/精灵。

来源和字节核对见 `offline-bundles-manifest.json`、`supplemental-tiles-manifest.json`、`supplemental-animation-manifest.json`、`historical-dependencies.json`。本次未加载全部二级特效依赖；样本之外的缺引用另行核对。

## 分层结果

| 层 / 类型 | 样本 | 实际结果 | 对资源库的意义 |
| --- | --- | --- | --- |
| 基础 · Texture2D | 4 张地图彩色/背景纹理、1 张法线图、2 张光照纹理 | GPU 采样并出图；包括原本不可 CPU ReadPixels 的压缩纹理 | 缩略图可以由 GPU 生成，不需要改原贴图的 Read/Write |
| 基础 · Sprite | `img_dark_ftg`、`sprite_charactercard_choesn`、`sprite_cost` | 3 项以 SpriteRenderer 显示，使用各 Sprite 的图集区域 | 索引必须保留 Sprite 子资源，不能把整个 Texture 当作图标 |
| 基础 · Material / Shader | `MT_LMC_common`、`MT_LMC_D`、`MT_LMC_Rain_BG`、`LMC_light`、`MT_LMZ_D`、`MT_LMZ_Deng2` | 6 个材质以原 Shader 出图，涵盖 Standard、Torappu Scene Standard、AlphaBlend、Additive | 原 Shader 及其依赖可在此宿主复用；仍按具体材质和渲染管线登记 |
| 基础 · Mesh / FBX | `bg_lmc.fbx` | 原 FBX 能显示几何，但默认材质没有地图背景纹理；显式绑定原 `MT_LMC_N_BG` 后显示背景 | 模型、材质与贴图是独立关联，不能凭 FBX 成功加载就认为绑定完整 |
| 基础 · Cubemap | `ReflectionProbe-0` | 六方向渲染不同画面，使用测试用 `Skybox/Cubemap` 展示 | Cubemap 数据可显示；尚不是原场景天空盒或反射探针行为验收 |
| 组合 · 粒子 Prefab | `absin_skill_02_start`、`absin_skill_02_hit` | 两项产生粒子并在固定相机下随时间显示/消失 | 可用原生 ParticleSystem 做预览；各有 4 / 1 个缺失脚本槽，完整技能调度未恢复 |
| 动态 · 材质动画 | `absin_skill_01_start`、`absin_skill_02_start` | 两段 Clip 改变 `wenli` 的 UV 和溶解量；独立画面改变；SampleAnimation 与 Animator.Update 同时刻逐帧哈希一致 | 动画不能只检查 Transform，需记录被驱动的材质属性 |
| 动态 · Legacy Animation | 电塔 `idle / activation / activation_idle / stop` | Clip 直接采样均出图；激活/停止各 4 种姿态，两个 idle 恒定；使用 FBX 白色 Default-Material，无贴图 | 原几何/Transform 动画通过，游戏材质绑定及 Legacy 组件播放未验收 |
| 动态 · Avatar + Clip | `[opt]start_fly`、`[opt]start_box` | Avatar 与 Clip 加载；Clip 直接采样，前者 4 种 Transform 姿态及画面，后者姿态恒定 | 未验证模型 Animator 状态机、Avatar 重定向；这些不是骨骼蒙皮模型 |
| 动态 · TrailRenderer | `absin_attack_01_trail` | 原样本与新建 Unity 对照组件均生成零宽几何，未显示 | 测试条件尚未闭合，不能归为原资源不兼容 |
| 场景 · 场景资源 | `level_main_05-10` | 场景 8 根对象、183 Renderer 实际出图 | 原地图场景的静态组合可显示 |
| 场景 · 烘焙光照 | 5-10 color + shadowmask | 179 Renderer 有有效引用；关闭光照图画面变化，恢复后 PNG 哈希与原图相同 | 烘焙图已参与渲染；移动/拆分模型时还需维护 UV2、索引和 scaleOffset |
| 场景 · LightProbes | 对象名 `LightingData` | 实际类型 `UnityEngine.LightProbes`，count / positions 均为 0 | 仅空对象加载；未验证有效探针照明，不是 LightingDataAsset 验收 |
| 逻辑 Prefab 对照 | `tile_road`、`tile_wall` | 可实例化，各 0 Renderer、1 缺脚本 | 地块逻辑 Prefab 自身不是可显示的地面模型；应关联地图场景图形 |
| 游戏脚本 | 5-10 组件 | 发现 198 个 null 组件槽，未恢复自定义 MonoBehaviour | MapGraphic / TileGraphic 等行为不能从静态出图推导为通过 |

### 动画属性的独立核验

`anim-sample-metadata.json` 将运行时曲线的对象路径/属性 hash 对应到 `static_offset/fixed/wenli` 的 MeshRenderer：材质 `absin_05`，Shader `Torappu/Particles/Dissolve/Dissolve Add`。编辑器曲线数组为空，但运行时 stream 曲线存在。

`bindings-04` 在不模拟粒子、只显示 wenli 网格的条件下，读取 `MaterialPropertyBlock`：

- clip01：UV 偏移 Y 为 0、0.20833、0.47222、0.89444；溶解量为 1、0.35、0.79434、1。
- clip02：UV 偏移 Y 为 0、0.75、1.5、2.7；溶解量为 0、0.4、0.4、0.85。
- 材质资源本身的值没有随采样改写；变化发生在 Renderer 的属性块。
- 两种原生采样方法在同一时刻得到相同 PNG 哈希。clip01 初始/末次采样的溶解量为 1，对应空画面；clip02 初始可见，末次采样量 0.85 仍有少量像素。

早期 `effects-02` 的 Transform 哈希恒定不能用于判定动画无效；该轮还混有粒子，已由上述独立核验补齐。

### 场景与光照

`scene-01` 使用包内完整虚拟场景路径启动失败；`scene-02` 与 `scene-audit-03` 使用场景短名成功。包内路径、SceneHashes 和 CAB 经离线核对未改变，失败记录保留。

`scene-audit-03`：

- 原光照图：`6adb712fc938c561588f925838a7b5342f30f6f54a95e4687f9257ff2465e84e`。
- 去光照图：`67b6a2c35e4661d4187681d62c34d11eebbe0e4bd3a5c22d3d65968bf633d9e4`。
- 恢复光照图：与原图相同。

Editor 辅助 API 的缺脚本计数为 0，但运行组件枚举有 198 个 null 槽，非空类型只有 Unity 内建组件。其数量与离线 MonoBehaviour 数量一致，尚未逐槽确认身份；不能将辅助 API 的 0 解释为脚本完整。场景中还存在一项空材质引用，静态画面成功不代表每个对象引用已恢复。

### 拖尾的诊断记录

`bindings-04`、`trail-05`、`trail-live-06` 均保留。原宽度 0.1，宽度曲线值 1，颜色/透明渐变和材质纹理可读。研究输入提供路径点、延长寿命；未恢复原 `ProjectileEffect` 的业务调用。

PlayMode 取得 50 顶点、48 三角形，但宽度方向每对顶点重合，原材质与诊断材质均为空画面。此结果定位到实际生成几何，不归因于贴图文件不存在。

`trail-control-08` 进一步对比原组件、重新赋宽度曲线的原组件、新建 Unity TrailRenderer；三者都出现相同的零宽几何。原包四层祖先缩放均为 `(1,1,1)`，见 `trail-sample-metadata.json`。因此不能将失败归于方舟 Prefab 或缺失 ProjectileEffect；后续需补齐预览中拖尾更新/运动输入条件。

## 证据与复现

| 目录 / 文件 | 内容 |
| --- | --- |
| `runs/static-01` | 原包负例、贴图、材质、Mesh、Cubemap、早期粒子/动画、逻辑地块 |
| `runs/scene-01` | 完整路径场景加载失败记录 |
| `runs/scene-02` | 短名场景加载、卸载 |
| `runs/effects-02` | 固定相机粒子时序；旧测试器在播放中设置种子，引发32条Error日志，保留原记录 |
| `runs/particles-09` | 修正为停止/清空后设置种子，只复测两项粒子；10张时序图、日志0条、Bundle清理后0 |
| `runs/scene-audit-03` | 场景前景相机、组件槽、光照关闭/恢复 |
| `runs/bindings-04` | 两种 Scene 材质、背景绑定、法线、Sprite、独立材质动画 |
| `runs/trail-05`、`runs/trail-live-06` | 拖尾及几何诊断 |
| `runs/trail-control-08` | 原组件、重赋宽度、新建 Unity 组件对照，三者均未显示 |
| `runs/transform-07` | Legacy / Avatar 模型动画 |
| `art-sampling-summary.json` | 报告/PNG 哈希复核、清理、草稿快照比较 |

测试工具存放在 [verification/art-resources](../verification/art-resources/README.md)，不进入 UPM 包编译。本轮所有结论以对应 JSON、PNG、日志为准；`completed` 只表示测试流程运行结束，不能当作该轮所有样本成功。

最终11轮报告保留了失败、诊断和补验过程，111条PNG记录的文件哈希全部匹配。各轮Bundle清理后均为0；三个成功创建运行场景的流程均完成场景卸载；11个本轮测试进程已退出。

主工作台前后快照文件SHA256均为 `3a73cb4d309048ecb9080c705b3a602f97628911deae380d7db087f10ecee4a3`，包含16项文档/草稿/历史相关记录；当前仍为“未命名地图4”，未进入PlayMode、未编译，主控制台错误0条，加载Bundle数0。快照只证明被记录的工作台状态未改变，不将其扩展为整个工程所有文件的快照。

## 后续接入顺序

1. 资源库先支持已证明能独立显示的 Texture、Sprite、Mesh、Material 和动画，保留包/子资源身份与依赖。
2. 地图场景另建预览场景，保留原光照数据和整体层级；与骨架渲染对象分别管理生命周期。
3. 动态效果区分纯 Unity 组件与游戏脚本初始化，逐项提供时间/输入预览。
4. 有效 LightProbes、原天空盒绑定、SkinnedMeshRenderer 和完整游戏脚本驱动效果继续作为独立样本，不从本轮结果外推。
