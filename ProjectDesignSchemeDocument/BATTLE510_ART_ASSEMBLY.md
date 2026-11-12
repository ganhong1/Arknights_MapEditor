# 5-10 局内美术组装与 Unity 预览

样本使用 PC 内置资源 `26-10-20-11-10-38-04170990`、Unity 2021.3.39f1、D3D11 与 Built-in Render Pipeline。范围为地图、光照、环境特效、红蓝门和预设装置。5-10 的模型由原场景与预制体在 Unity 实时绘制。离线 PNG 由同一场景的相机输出，用于对照。

## 从原包到可见对象

| 步骤 | 已验证的组装方法 | 核对点 |
| --- | --- | --- |
| 解包 | 使用现有 Unity-JS 的 Arknights 读取模式，将副本写成标准 UnityFS，保留包内节点、Unity 版本和目标平台 | 原包直接加载会报压缩格式 4；转换后逐节点比对并核对原包 SHA256 |
| 补依赖 | 从 SerializedFile 外部引用取 CAB，在本地资源索引中定位唯一提供者，优先使用同版本现装包 | 分层样本闭合为 41 包，其中 14 个二级包和 4 个重新定位的 CAB 来源。按资源版本区分历史副本。MonoScript 元数据只提供脚本类型身份 |
| 加载场景 | 按样本依赖加载 UnityFS 副本，再用包内场景短名加载 `level_main_05-10`，使用原场景 Camera | 183 个 Renderer，179 个有有效光照图引用。关闭再恢复光照图的画面对照验证了绑定。拆分场景模型时须保留 UV2、lightmap 索引与 scaleOffset |
| 组装背景与环境 | 保留场景中的模型、贴图、材质、Shader、光源及烘焙配置。对需要游戏初始化的夜雾，单独加载并采样原 `screen_lmc_fog_02` | FBX 自带材质与地图实际绑定可能不同。粒子、Transform 动画与材质属性块应各自核对 |
| 恢复红蓝门 | 以逻辑格心对齐 `tile_start` / `tile_end`，从 `MeshColorizer._color` 恢复实例网格顶点 RGBA，保留原网格形状、材质与 Animator。按 `FxDelay._delayTime` 取样可见状态 | 6 红门、2 蓝门、32 个顶点色网格。0.25 秒与 0.50 秒对照覆盖 0.40 秒延迟。蓝门装饰锚的偏移与逻辑格心分开 |
| 组装弩炮 | `trap_007_ballis` 定义提供逻辑身份，`MapDependentTrap` 按 `LMC_NIGHT` 选 `skin_ballis_night`。从 `predefines.tokenInsts` 读坐标、朝向、别名和隐藏状态，按 `branches.faust_ballis` 的 `ACTIVATE_PREDEFINED` 对应别名 | 开局 0 台可见。首台取 `bolt_born` 0.20 秒姿态及 `bow_birth_01_start`，全激活取 10 台原夜景皮肤及 `bolt_buff_01_start`。每台核对 6 个 Renderer 的材质、纹理与 Shader |
| 释放 | 离线渲染结束后卸载测试场景，再释放本轮加载的资源包 | 51 包加载成功，8 张 PNG 与报告哈希一致，清理后包计数为 0。交互预览运行期间保留这些资源，退出 Play Mode 恢复预览时间设置 |

## 交互预览

独立工程 `UnityProbeProject` 使用 `ArknightsArtProbe.OpenBattle510Preview` 和 `-probeMode battle510-map`，另传 `-probeRoot` 与新的 `-probeOutput`。Game 视图默认显示已激活装置，`Tools → 5-10 Preview` 可切换开局与装置已激活状态。模拟时间冻结，方便检查地图构图。

初版在 `EditorApplication.update` 反复读取 `Screen.width/height` 并写回相机 `rect/aspect`。Game 视图重绘时会同时管理渲染目标尺寸，导致画幅由两处更新。新版只在初始化时将相机设为全视口并调用 `ResetAspect()`，在 Game 视图选用 Unity 原生 16:9。重新打开后两次移动鼠标检查，地图、门和装置位置保持稳定。此项是交互复查，未做连续逐帧抖动测量。

## 接入美术库时可复用的部分

- 保存资源键、包内对象路径、CAB、平台和版本。以同一资源身份查依赖，目录只负责索引，预览持有实际场景及对象实例。
- 地块逻辑配置由 LevelData 提供，模型及材质来源由场景美术提供。装置逻辑身份、主题选皮肤、格心与朝向各保留独立记录。
- 分开验证依赖闭合、可加载、材质绑定、动画取样和游戏脚本行为。5-10 预览手动应用已核对的初始化输入，未验证游戏触发时钟、开火逻辑或同版本同帧像素一致。主编辑器草稿与游戏原包均保留。

## 证据索引

本机证据根为 `D:/Temporary_Embedded_Project/storage/map-editor-unity/art-probe-20261109`。下列路径相对此根。

| 记录 | 路径 |
| --- | --- |
| 转换与依赖闭合 | `DEPENDENCY_CLOSURE_RETEST.md`、`dependency-closure-verification.json` |
| 门色对照、弩炮来源和批处理清理 | `BATTLE510_MAP_RENDER_RETEST.md`、`runs/battle510-map-02/report.json` |
| 交互复验 | `runs/battle510-map-visible-02/report.json` |
| 门与装置序列化取证 | `battle510-gate-prefabs.json`、`battle510-device-prefab.json`、`battle510-device-skin.json` |
| 探针源码 | `UnityProbeProject/Assets/Editor/ArknightsArtProbe.Battle510Map.cs`、`ArknightsArtProbe.Battle510Preview.cs` |
