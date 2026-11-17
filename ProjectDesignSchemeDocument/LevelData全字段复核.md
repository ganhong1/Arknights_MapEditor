# LevelData 全字段复核与剩余字段研究

基线为 PC 2.7.81。字段声明、原生默认分支、历史 JSON 和编辑器能力分别记录。冻结成品库的 8,664 份代表正文来自多个数据来源和历史版本，同一正文可以被多个关卡、模式或来源复用。

## 来源与复验

| 内容 | 固定依据 |
| --- | --- |
| C# 声明 | APW 的 `source/ark-mix-source/DumpCS/2.7.81/dump-pc-x64-source.cs`，SHA-256 `0d5e3251c69bad5e9c44ed3a027f066950dc703a3ffc00f0c88df1f93602ff52`。LevelData 从 102722 行开始。 |
| 原生实现 | GameAssembly.dll，SHA-256 `583b04ff4d1b1a4b35eaf09d77d936408c25447c9e345ef0463d814b6a123fc5`。配套 metadata SHA-256 `d3fede171f92740a6f5dc053c58c7013d3c962db764ad1f05cbe977c074fcd1f`。 |
| 历史正文 | Unity 包内 `Editor/Catalog/finished-levels.zip`，SHA-256 `a3e15a7e7a526f661fa34d42fc861856ec35083106781125fa566bd587392e45`。 |
| 研究产物 | `D:/Temporary_Embedded_Project/storage/map-editor-unity/leveldata-field-audit-20261114`。`structure.json` 为全量字段形态，`declared-model.json` 为类型与枚举图，`related-structure.json` 为路线、演出与分层样本。 |
| 字节收据 | 93 个原生函数地址，51,920 字节。研究目录内各 `native-consumer-receipt.json` 及 `.asm`、`.c`、`.bin`。复用既有研究 IDA 库，关闭自动分析，关闭时不保存。 |

反编译可能对共享泛型和同地址方法误命名。字段和方法的判断以参数布局、调用指令与磁盘字节为准。

## 全字段导航

[字段与类型附录](LevelData原生字段与类型.md) 完整列出 22 个持久字段的 FlatBuffers 偏移、正文状态，以及嵌套类型、继承、运行字段和枚举。

| 根字段 | 原生类型 | 读取或相关专项 |
| --- | --- | --- |
| `options` | `LevelData.Options` | 规则配置，15 个持久字段，3 个运行成员。[值域研究](全局设置值域研究.md) · [黑板约束](全局设置黑板逐键约束.md) |
| `levelId` | `string` | 正文身份与运行请求 ID。 |
| `mapId` | `string` | 场景回退键。 |
| `bgmEvent` | `string` | 开场音乐子信号，外部输入可覆盖。 |
| `environmentSe` | `string` | 开场环境音子信号。 |
| `mapData` | `MapData` | 格索引、TileData、阻挡边、标签、特效、图层。[地图数据设计](地图数据编辑设计.md) |
| `tilesDisallowToLocate` | `List<GridPosition>` | 关卡初始化覆盖部署类型。[规则研究](地图规则原生读取研究.md) |
| `runes` | `LegacyInLevelRuneData[]` | 关卡符文，有难度有效性检查。 |
| `optionalRunes` | `Dictionary<string,List<LegacyInLevelRuneData>>` | 按外部组名选择，保留组内顺序。 |
| `globalBuffs` | `GlobalBuffData[]` | 全局效果配置，六个持久字段。[三类符文输入](符文与增益原生结构研究.md) |
| `routes` | `RouteData[]` | 主路线、运行游标、检查点。 |
| `extraRoutes` | `RouteData[]` | 额外路线，结构与主路线相同。 |
| `enemies` | `LevelData.EnemyData[]` | 内联敌人数据。 |
| `enemyDbRefs` | `EnemyDataDbReference[]` | 数据库引用与独立覆盖定义。[敌人读取与合成](敌人资源库数据设计.md) |
| `waves` | `WaveData[]` | 波次、片段、动作。 |
| `branches` | `ListDict<string,BranchData>` | 分支键、阶段与动作。[调度研究](波次与分支数据及编辑设计.md) |
| `predefines` | `PredefinedData` | 干员／装置的场上及卡牌输入。 |
| `hardPredefines` | `PredefinedData` | 相同字段树，按困难预设合成入口决定实际选用。[预设结构](LevelData结构与编辑边界.md#3-角色与装置预设) |
| `excludeCharIdList` | `string[]` | 编队与助战检查。 |
| `randomSeed` | `Int32` | 关卡正文值。普通开局、回放、玩法可以给运行层传入各自的种子。 |
| `operaConfig` | `string` | 演出配置资源键。 |
| `cameraPlugin` | `string` | 已定位集成战略场景的读取分支。 |
| `runtimeData` | `LevelData.RuntimeData` | `NonSerialized`，内部 `List<EnemyDataDbReference> extraLoadEnemies`。 |

## 关卡身份、正文和场景

`ResourceRouter.GetLevelDataPath`（`0x1808F3420`）按请求 `levelId` 查 BattleMiscDB，命中时以映射值的 `levelId` 形成 `gamedata/levels` 下的正文路径。`LevelUtils.LoadLevel`（`0x180AFEE90`）解码后把原请求 ID 写入运行中的 `LevelData.levelId`。`LevelUtils.GetSceneName`（`0x180AFEB60`）再以运行 ID 查 `sceneId`。映射缺失或场景键为空时调用 `LevelData.GetSceneName`（`0x18241C380`），按 `mapId`、`levelId` 回退。

| 标识 | 归属 |
| --- | --- |
| `stageId` | 关卡表或玩法入口的业务身份 |
| 请求 `levelId` | 调用者传入的关卡资源键 |
| 映射 `levelId` | `levelScenePairs` 的正文重定向目标 |
| `sceneId` | 同一条映射中的战斗场景资源键 |
| `hookedMapPreviewId` | 映射中独立的地图预览键 |
| body hash | 成品库正文存储标识 |
| 工作区文档 ID | 编辑器的草稿、撤销、视口关联 |

`Activities/ACT21side/Mission/level_act21side_01_m` 可以重用 `Activities/ACT21side/level_act21side_01` 正文与场景，地图预览键为 `act21side_01`。Boss Rush 样本也出现正文与场景键不同的映射。当前本地 GameData 有 586 条映射。582 条的正文目标可用路径归一化在成品库找到。312 个成品库关联关卡的请求键与源表映射键精确相等，且都能找到正文目标。

冻结库中 8,664 份 `mapId` 都为 null。27 份 `levelId` 持有 `[dev]test/level_training`，其余 8,637 份为 null。资源页应同时呈现请求身份、正文来源与场景选择结果，允许独立编辑显式 `mapId`。

映射键使用字典的 `TryGetValue`。路径归一化用于离线关联目录。两份输入表的 SHA-256 见 `scene-linkage.json`。

`StageDataUtil.LoadStagePreviewMap`（`0x18294D650`）默认使用 `stageId`，按关卡的请求 `levelId` 命中映射时改用 `hookedMapPreviewId`，随后拼接 Sprite 资源路径。方舟图鉴关卡详情与保全派驻也有独立读入口。该键服务于游戏地图预览图，骨架预览仍由编辑器渲染正文。

## 构造、反序列化和运行数据

- `LevelData` 构造器（`0x18241C3B0`）初始化 `options`、五个空数组 `routes/extraRoutes/enemies/enemyDbRefs/waves`、`runtimeData`，并写 `randomSeed=-1`。`MapData` 构造器初始化 0×0 `short[,]` 和空 `TileData[]`。编辑器新建模板有自己的默认值。
- `LevelUtils.ParseLevelData`（`0x180AFF000`）解码 TextAsset。所核对的 DBOptions 分支为 1 时使用 FlatBuffers converter，其余走 JSON_DOT_NET。
- `Unpack_Torappu_LevelData`（`0x183B97FC0`）在构造后逐字段赋值。`randomSeed` 使用 `UnpackInt(table,42,0)`，因此构造器默认与此解包入口的缺省参数不同。缺失的 `options` 解包为 null，可覆盖构造器创建的默认对象。MapData、TileData 和 MapEffectData 各有自己的解包方法。
- `runtimeData` 构造时初始化 `extraLoadEnemies`。生息演算的 `SandboxLevelDataProcessor.Init`、`_ParseMiscLevelData` 和 `_ParseRushEnemyAction` 都可向此列表追加数据库引用，输入来自玩法预载敌人、常量和动态出怪配置。`Scheduler.Init` 消费该列表以追加敌人定义，实际出场由相应动作或玩法入口驱动。冻结正文中 3,908 份为 null，其余缺失。
- `LevelTempData.AssignData`（`0x18241C5E0`）写回 `routes`、`extraRoutes`、`enemies`、`enemyDbRefs`、`waves`、`branches`。`LevelTempData` 的声明还有 `runes` 与 `globalBuffs`，该方法未对这两项赋值。
- `LevelUtils.LoadLevelScript` 可加载一种 LevelData 正文。`LevelScriptUtils` 与 `LevelScriptDB` 另有脚本资源映射。演出配置、脚本键、动作键和分支键各按消费入口解析。

## 音乐与环境音

`BattleUtil.GetBgmEvent`（`0x180AE0C40`）优先取运行输入的非空 `overrideBgmEvent`，再取正文 `bgmEvent`。`BattleAudioLoader.PreloadOthers`（`0x1808E6510`）按 `ON_GAME_READY` 预加载。开局的 `useLevelBgm` 分支将音乐子信号交给 `BattleBGMManager.Play`。

非空 `environmentSe` 按 `ON_ENV_SHOW` 预加载，开局调用 `TorappuAudio.PlayBattle(ON_ENV_SHOW, environmentSe)`。`PLAY_BGM.key` 属于独立的 `ON_CUSTOM_TRIGGER` 信号。

| 表 | 294 个背景音乐子信号 | 两个环境音子信号 |
| --- | --- | --- |
| GameData `audio_data.json` | 282 直接，10 通过 alias，2 未匹配 | beach 与 magma 直接匹配 |
| GameResource `audio_data.json` | 283 直接，10 通过 alias，1 未匹配 | beach 与 magma 直接匹配 |

两表共同缺 `bat_shatterpoint_02`，涉及 2 份正文。17 份中的 `act3break` 可从 GameResource 补到。环境音为 `beach` 36 份、`magma` 11 份。音频表路径和 SHA 见 `audio-resolution.json`。BGM bank 含 intro、loop、音量、crossfade、delay、fadeStyleId，环境音 soundFX bank 含声音列表、循环、抢占、mixer 和单声音的资源、权重及音高范围。字段编辑保存子信号，完整事件名与音频资源用于定位和将来的试听。

将事件库与本地 PC 包清单继续关联后，294 个 BGM 键中有 256 个可以在至少一份来源里找到所需的全部片段和本地包，37 个已有事件记录且片段未命中此清单，1 个尚未在两个事件表中匹配。两个环境音均命中 `audio/sound_beta_2/ambience.ab`。536 个唯一片段路径有 469 个命中。事件存在、包文件存在与可试听分别核对。具体路径见 `audio-clip-manifest.json`。

在 252 个已命中的本地包中，进一步逐一读取容器指向的对象，469 个命中片段均能定位到 Unity AudioClip（ClassID 83）。`audio-clip-object-receipt.json` 记录包哈希、CAB、PathID 和类型。

2026-11-15 将 469 个既有片段和 9 个额外换曲片段全部从 FSB5 解码为 PCM16，并导出为 FLAC。478 项均与 AudioClip 元数据匹配，FLAC 回读 PCM 与解码结果逐字节一致。事件覆盖为开局音乐 256/294、环境音 2/2、波次与分支换曲 17/24。换曲缺项中 3 个事件缺片段，4 个缺 `ON_CUSTOM_TRIGGER` 绑定。产物存于 `D:/Temporary_Embedded_Project/storage/map-editor-unity/audio-decode-20261115`，组合索引为 `audio-event-decode-index.json`。Unity 播放控件另行接入。[解码复现流程](../unity/verification/audio-resources/README.md)

2026-11-16 以 TOR 导出和哈希匹配的历史官方包补齐 71 个独立片段，合计 549 个。具有明确事件绑定且所需片段齐全的事件升至开局音乐 293/294、环境音 2/2、波次和分支换曲 20/24。剩余五个事件的参考曲也有前奏与循环段，索引单独保存其试听关联和事件绑定状态。合并索引为 `audio-event-decode-index-20261116.json`，各片段留存平台、版本（如可核对）、下载来源与校验摘要。

## 地图分层、标签与效果

### layerRects

每项为 `(起始行,起始列),(结束行,结束列)`。固定 DLL 中 `GlobalConsts.RECT_REGEX` 对应 metadata 字符串 24147，允许带负号的整数或小数，正则没有整串锚点。

```text
\((-?\d+(\.\d+)?),(-?\d+(\.\d+)?)\),\((-?\d+(\.\d+)?),(-?\d+(\.\d+)?)\)
```

`Misc.ParseRect`（`0x186727E20`）取组 1/3/5/7，按 `Convert.ToSingle` 得到四个坐标。运行 `Rect` 用起点和两端之差。无匹配时记录错误并返回零 Rect。

`Map.ImportData`（`0x18087E720`）按数组索引指定 `MapLayer`，地块取第一项匹配的矩形，无匹配回退 A。`MathUtil.Contains` 以 `(row,col)` 检查范围，并使用约 `1e-5` 的容差。另一路 `Map.CheckWithinLayerRect` 比较原 Rect 上下边界，两端包含，空集合返回 false。`Map._InitMapLayers` 计算和保存各层中心，并翻转坐标供相机、玩家侧和演出消费。声明的层为 `NONE=-1/LAYER_A=0/LAYER_B=1/E_NUM=2`。

9 份非空正文来自 `act17d1` 的三张图和 a/b/c 变体，每份两层。18 项原文均通过固定正则的离线对照。三张图的每层覆盖格数分别为 91、96 和 88。应提供矩形原文、A/B 次序、坐标和受影响格子，裁剪时报告范围变化。新增第三层没有原生枚举支持证据。

### tags

`Map.CheckHasTag`（`0x18087A460`）查询原字符串列表。`BattleController.ContainsMapTag`（`0x18061CA70`）先读 LEVEL 黑板同名整数，正数直接通过，否则回到地图标签。

`Map._InitControllerByTags`（`0x180883290`）使用 `OrdinalIgnoreCase` 与枚举名匹配，工厂默认只为 `NIGHT`、`MAGICCIRCUIT` 创建地图控制器。语料中 20 个不同标签、24 种非空组合。水域、活动、主题名继续适用字符串读取。编辑应保留原次序、大小写和未知标签。

### effects

```text
MapEffectData
├─ key          string
├─ offset       Vector3 → x / y / z
└─ direction    SharedConsts.Direction → UP=0 RIGHT=1 DOWN=2 LEFT=3
```

`BattleController._InitCameraAndMapEffects`（`0x180638A70`）创建正文地图效果，再创建场景 MapGraphic 的效果列表。`Tile.GenerateTileEffect`（`0x1808BDA70`）读所在格共享的 TileData.effects，以运行格作为 locator。`Tile.Start` 和部分展示区域变化均可进入该方法。

`CreateMapEffect`（`0x1806216A0`）将 offset 加到定位点，缺失 locator 时用零向量，然后设置父对象。运行组件为 MapEffect 时，`MapEffect.Play`（`0x1811DE230`）叠加预制体 `_spawnMapOffset`，再以 `direction` 索引 `FOUR_WAYS`。保存已有原文时保留历史值，新增可执行配置仅提供 0–3，原生越界会抛出异常。普通 Effect 走自己的 Play 路径。`ResourceRouter.GetEffectPath` 提供 `battle/prefabs/effects/<key>` 入口，最终包归属看实际资源容器。

全部 `mapData.effects` 都是 null 或空列表。TileData 有 1,058 条非空列表，每条恰好一项，涉及 16 个效果键。条目均有完整 `key/offset/direction`，方向 607 条用名称、451 条用数字。地图效果与地块效果可以共享字段表单，分别提供场景定位与共享配置的对应格预览。

### 实际资源与对象引用

PC 本地资源清单 `26-10-20-11-10-38-04170990` 包含 13,596 个包和 132,134 个资源条目。资源清单文件为 `41ae9b221e84c6ff47871ca907c538d2.idx`，SHA-256 为 `237d76bb32fde6c2810574fbdc581593bca268a8245d2b03970839f66c301287`。效果键按 `battle/prefabs/effects/<key>` 查表，实际容器为 `dyn/battle/prefabs/effects/<key>.prefab`。

| 正文中的资源 | 实际资源包 | 清单声明依赖 |
| --- | --- | ---: |
| 14 个地块效果键 | `battle/prefabs/effects/map.ab` | 29 |
| `mapeff_v077_change_01` | `pkgrps/btl_pfb_fx_mapeff_10.ab` | 27 |
| `mapeff_waterTernado_01` | `pkgrps/btl_pfb_fx_mapeff_7.ab` | 28 |
| `rogue_4_boss4` | `ui/rglktopic/rogue_4.ab` | 35 |

17 个容器的目标全部为 GameObject。组件的 MonoScript PPtr 显示 16 个地块效果使用 `Torappu.Battle.Effects.MapEffect`，相机使用 `Torappu.Battle.UI.CameraRoguelikePluginRL04Boss4`。17 个容器共 558 条预加载引用。433 条在主包内找到对象，45 条在安装目录的声明依赖包中找到对象，78 条在两份本地匿名 CAB 缓存中找到对象，2 条指向 Unity 引擎资源。`prefab-preload-object-receipt.json` 记录逐项 CAB、PathID、ClassID 和包 SHA-256。两个匿名 CAB 的文件来源为 `ArknightsUIResearch/Bundles/anon`。引擎内置对象和运行实例化需要各自验证。

引擎引用分别来自 `mapeff_v077_change_01`（PathID `10202`）和 `mapeff_waterTernado_01`（PathID `10210`），均属于 `Library/unity default resources`。复测切片可优先选这两项与一个普通 `map.ab` 地块效果。

## 编队排除、种子、相机与演出

| 字段 | 当前证据 | 编辑边界 |
| --- | --- | --- |
| `excludeCharIdList` | `ExternalRuneChecker._LoadDataImpl`（`0x18170C710`）将正文数组存入普通排除列表，另外处理难度和选中的符文。`LoadExcludedCharInSquad` 加载命中条目，出战检查覆盖编队与助战。 | 提供干员 ID 有序列表和本关关联。历史 4,967 份 null、3,697 份空，非空行为需要专用合成样例验证。 |
| `randomSeed` | 8,664 个整数，2,624 个不同值，226 个 `-1`。普通 `LoadGame` 生成种子，回放从日志读取，玩法可以改传入种子。 | 原始 Int32 与预览种子分开保存。以固定输入、玩法和随机源共同描述复现条件。 |
| `cameraPlugin` | `GameModeFactory.LoadCameraPlugin`（`0x180BE1C80`）在集成战略读取该字段，经 `RoguelikeBattleUtil.LoadCameraPlugin`（`0x180D0A0E0`）组合主题和插件名。唯一非空样例 `rogue_4_boss4` 有 6 份。 | 按主题和资源版本选择，保留缺失/null/空串。场景视口和 env_system_new 的相机参数属于各自的入口。 |
| `operaConfig` | `config/leveloperaconfig/<key>` 的 OperaConfig 对象。`PLAY_OPERA.key` 选择其中命令，节点接收该动作参数。 | 配置与命令分别提供选择和定位。仅已识别读取路线的节点参与重排预检。 |

集成战略插件路径为 `UI/RoguelikeTopic/Topics/{topicId}/CameraPlugin/{cameraPlugin}.prefab`。CameraController 先取玩法侧资源，未取到再尝试活动和关卡相关插件。后一路从 `env_system_new` 的 `camera_plugin_name` 读 `UI/LevelRelatedCameraPlugin/{name}`，已有插件时退出。UIController 的 `ui_plugin_name`、`ui_plugin_path` 也有自己的读取路径。

OperaConfig 持有 `_commands: List<OperaCommand>`，命令三字段为 `key`、`operaNodes`、`duration`。节点继承 `_preDelay`，具体类型决定额外参数。`PlayAudio`、`PlayEffect`、`PlayMapEffect` 和 `FinishTileHoldEffect` 可读取动作的路线位置。`TriggerEnvSystemOpera` 将完整动作参数传入环境系统。

219 份正文有 `operaConfig`，共 19 个键。已有 config/common.ab 提取物包含全部 19 个。18 个配置出现在本语料的 `PLAY_OPERA` 中，643 条动作对应 36 个配置／命令组合，全部找到命令。40 份有配置的正文没有该动作。资源可按配置引用独立存在。

## 路线结构与 11 个有效检查点

主、额外路线使用同一 `RouteData`。10 个字段包括运动类型、起终点、出生偏移/随机范围、检查点数组和四个路径选项。单个检查点的六字段见 [LevelData 总结构](LevelData结构与编辑边界.md)。

| 类型 | 主路线 | 额外路线 | 消费含义 |
| --- | ---: | ---: | --- |
| MOVE 0 | 770872 | 40555 | position、reachOffset、randomizeReachOffset；位置到达分支的 reachDistance 有 0.05 下限。 |
| WAIT_FOR_SECONDS 1 | 189785 | 7220 | 自检查点进入后等待 time。 |
| WAIT_FOR_PLAY_TIME 2 | 0 | 0 | 固定游玩时间与 time 比较。 |
| WAIT_CURRENT_FRAGMENT_TIME 3 | 10240 | 4 | 用户固定时间相对片段开始快照。 |
| WAIT_CURRENT_WAVE_TIME 4 | 1551 | 5 | 用户固定时间相对波次开始快照。 |
| DISAPPEAR 5 | 34587 | 1547 | 消失检查点。 |
| APPEAR_AT_POS 6 | 34801 | 1759 | position 和 reachOffset 决定出现位置。 |
| ALERT 7 | 188 | 0 | 以当前个体所在格播放警示音与效果。 |
| PATROL_MOVE 8 | 32621 | 1361 | Move 派生，游标可以回索引并有循环保护。 |
| WAIT_BOSSRUSH_WAVE 9 | 328 | 60 | time 转 Int32 参与玩法 Boss 波索引，不按秒数解释。 |
| MAP_OFFSET_MOVE 10 | 3868 | 0 | 在 position 基础上加地图移动偏移和 reachOffset，继承巡逻处理。 |
| INVALID 11 | 0 | 0 | 默认解析工厂无法创建，抛出 NotImplementedException。 |

两库合计 1,131,352 条检查点。另有主路线 null 槽 1,008 个、额外路线 null 槽 62 个。保留这些位置，避免过滤后改变已有索引。

## 历史正文兼容

| 样本 | 成品库状态 | 编辑处理 |
| --- | --- | --- |
| `mapData.map`、`tiles` | 两份空模板的 map 为 null。其中一份 tiles 也为 null。其余 8,662 份有格子；共 1,039,935 个格索引。 | 空模板单独标记。 |
| 历史 `mapData.width/height` | 各 232 份，全部等于矩阵推导值。 | 读写保留原文。新尺寸从矩阵得到。 |
| `waves[].name`、`advancedWaveTag` | `name` 出现在 476 个波次，127 个字符串，349 个 null。`advancedWaveTag` 有 1,574 个字符串、13,783 个 null、142 个缺失。 | 保留历史 `name`，`advancedWaveTag` 继续按玩法入口解释。 |
| 动作的 `useExtraRoute`、`weightValue` | 主动作分别有 1,026 和 1,093 条，分支动作有 71 和 5 条。 | 保留历史值；显式编辑路线时按动作、路径和调度上下文校验。 |
| 分支阶段三个 `m_` 缓存字段 | 每个字段都有 4,738 条带值、250 条 null。 | 作为历史 JSON 保留，缓存由运行层生成。 |
| `options` 的三个运行字段 | `enemyTauntLevelPow` 3,486 份，两个费用增量字段各 2,983 份。 | 原样保留已导入值，排除出新建模板。 |
| `enemies[].m_runtimeData` | 96 条有键，全部为 null。 | 作为历史原文处理。 |
| `globalBuffs` 的三个运行字段 | 976 条含 `useExtraData`，886 条 `extraRuntimeData` 为 null，684 条 `source` 为 null。 | 保留历史原文，运行附加参数由消费方提供。 |

逐项计数与代表路径见 `compatibility-shapes.json`。`bat_shatterpoint_02` 另外对照了 OpenBachelorS 的三份历史音频表，匹配到反向线索 `shatterpoint_02`。同名片段均已定位到 AudioClip，尚无 `bat_shatterpoint_02` 指向该事件的原生别名证据，两键分别保留。

## 后续编辑与证据边界

地图标签可先提供候选和有序表单。分层矩形可提供 A/B 视图和几何预检。效果复用资源选择、偏移及方向表单。场景、音频、演出、相机按各自资源键和版本解析。编队排除与原始种子分别作为文档字段编辑，并复用现有草稿、撤销与恢复。

| 待实现的入口 | 数据与校验契约 |
| --- | --- |
| 地图标签与图层 | 每条保留 JSON 路径、原文和次序。图层预检显示两种原生边界算法的差异，重排预览先标出归属变更；裁剪前列出受影响格。现有 MapData 面板仍使用原文与空间提示。 |
| 地图及地块效果 | 两个入口共用三字段表单。地块效果编辑显示该配置引用的全部格，地图效果从地图原点预览。保存历史未知资源时保留键，验证新效果时分别报告目录、对象、依赖与实际表现。 |
| 资源引用 | 以对应原生键和资源版本解析。背景音乐和环境音根据事件族过滤。相机按主题查找。演出配置变化时列出受影响的 `PLAY_OPERA`，分别报告命令缺失和已知路线引用；未知命令保持原文。 |
| 编队排除与种子 | 排除列表用干员 ID 选择器，保留输入原序；对重复值和未解析 ID 提示。种子用原始 Int32，复现工具单列运行种子和来源。 |
| 文档事务 | 单字段应用复用现有 `LevelDocument`、文档身份／版本及撤销。回归至少涵盖缺失、null、空值、未知键、历史空路线槽、双文档切换和草稿恢复。 |

待复验项包括引擎内置资源的对象实体、效果和相机的实际实例化与玩法覆盖、非空编队排除端到端行为、外部脚本及热更新。本文的统计、文件比对和静态解析属于离线研究。完整收据的校验明细见证据目录内 `evidence-manifest.json`。
