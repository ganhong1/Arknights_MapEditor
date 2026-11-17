# LevelData 字段与编辑器落地复盘

复核日期 2026-11-15。原生结构以 PC 2.7.81 的 `Torappu.LevelData`（TypeDefIndex 4680）及其关联类型为准。编辑实现以 Unity 功能工作树为准。成品库的历史正文和 2.7.81 原生字段基线各有独立来源。

## 顶层字段

按原生声明顺序列出 22 个可序列化字段。12 个字段已接入结构化编辑，7 个字段支持单独编辑 JSON 片段，3 个字段通过全量 JSON 编辑。mapData 内部的覆盖程度另列。中间字段树展示已应用文档，实际编辑入口在左侧模块或「原始 JSON」。全量 JSON 应用经过文档解析和核心检查。

| 字段 | 原生类型或形态 | 当前入口 | 已落地的能力与边界 |
| --- | --- | --- | --- |
| `options` | `Options` | 全局设置 | 15 字段表单按原序排列，支持构造默认对象、样例值与出处、自定义值、缺失与 null，保留原始 JSON。详见下表。 |
| `levelId` | `string` | 全局设置第一项 | 单行输入后应用，支持撤销。成品副本写入已匹配的原版请求 ID，其中包含正文重定向场景；没有匹配记录时显示并使用所选正文的资源键。 |
| `mapId` | `string` | 高级片段、全量 JSON | 可编辑原始值，当前没有把它接到通用 Unity 场景解析或美术选择器。 |
| `bgmEvent` | `string` | 背景音乐 | 左侧编辑开局事件键和原始 JSON。右侧「音乐」资源库提供按用途／按原版事件浏览、选曲、详情和试听。局中换曲与波次和分支共用动作草稿和撤销记录。 |
| `environmentSe` | `string` | 环境音效 | 专用 JSON 片段、应用、重读。可从右侧音效库选用 2.7.81 的 beach 或 magma，选用写入现有草稿。 |
| `mapData` | `MapData` | 地图数据 | 六字段分栏，网格、共享 TileData 与阻挡边可结构化编辑，其余三项支持各自的 JSON 片段。详见下表。 |
| `tilesDisallowToLocate` | `List<GridPosition>` | 地图数据 · 禁止部署格 | 坐标表单、点选/拖动/框选、独立叠加显示、撤销与裁剪检查。仍保存于 LevelData 根；初始化时把对应运行格 buildableType 覆盖为 NONE，不影响敌人通行。 |
| `runes` | `LegacyInLevelRuneData[]` | 符文与增益、高级片段 | 左侧模块摘要与字段树定位，编辑用高级 JSON。待做原版定义、引用和黑板编辑。 |
| `optionalRunes` | `Dictionary<string,List<LegacyInLevelRuneData>>` | 符文与增益、高级片段 | 保留键分组及原文状态。待做分组管理、条件与引用检查。 |
| `globalBuffs` | `GlobalBuffData[]` | 符文与增益、高级片段 | 摘要与 JSON 片段可用。待做逐项效果和参数表单。 |
| `routes` | `RouteData[]` | 路线、高级片段 | 场景绘制、检查点类型与参数表单、路线增删重排、已知引用更新和 PRTSMap 式演示。 |
| `extraRoutes` | `RouteData[]` | 路线、高级片段 | 独立存放和编辑。默认调度按主波次／分支上下文确定路线库，历史 `useExtraRoute` 保留原值。 |
| `enemies` | `EnemyData[]` | 敌人定义 · 内联定义 | 支持新建、复制、增改删和撤销。身份、属性、天赋、技能及技力有表单，完整 JSON 保留原生字段。以 `alias` 或 `key` 检查冲突，确认后同步已识别的敌人动作引用。常规调度和模式查询的优先级见 [敌人专项复核](敌人资源库数据设计.md#内联敌人的读取入口同键优先级与版本沿革)。 |
| `enemyDbRefs` | `EnemyDataDbReference[]` | 敌人定义 · 引用定义、高级片段 | 数据库引用／独立定义，四字段与覆盖编辑、完整 JSON、合成预览、增改删和撤销。技能、天赋和技力使用覆盖表单。更名同步 SPAWN/DISPLAY_ENEMY_INFO，引用保护删除；未知字符串关联需先处理。 |
| `waves` | `WaveData[]` | 波次、高级片段 | Wave、Fragment、Action 三层字段树支持增删、复制及上下移动。动作统一使用 ActionDataForm，19 字段可编辑；新增按类型校验，本关敌人、主路线、预设别名和资源候选可选，片段显示相对时间。见 [首版工作台操作](波次与分支数据及编辑设计.md#首版工作台操作)。 |
| `branches` | `ListDict<string, BranchData>` | 分支、高级片段 | 分支／阶段／动作卡片编辑已接入，ActionDataForm 与 waves 共用字段表单和 ActionDataRules 校验。支持新建、复制、重命名、删除与重排、动作草稿、撤销恢复、额外路线和预设别名选择。已解析调用同步与 action_index 保护，外部及未知关联列出位置。5-10、AW-6 已验证。 |
| `predefines` | `PredefinedData` | 关卡预设 | 基础预设，四类记录分开编辑，支持场上、卡牌、资源库选用、编队复制和手牌预览。 |
| `hardPredefines` | `PredefinedData` | 关卡预设 | 困难预设使用同一套控件并独立读写。原生 FOUR_STAR 且困难训练标记开启、困难预设存在时，只采用困难干员卡牌，其余三组取基础预设。详见 Waves 五项复核。 |
| `excludeCharIdList` | `string[]` | 全量 JSON | 保留已有字段。待做以干员 ID 为值的多选与重复检测。 |
| `randomSeed` | `int` | 全量 JSON | 可随正文保存，当前无独立 Int32 控件和编辑器内的随机结果回放。 |
| `operaConfig` | `string` | 全量 JSON | 保留原值。已核对本地演出配置和命令；专用引用表单及部分节点的路线重排校验待接。 |
| `cameraPlugin` | `string` | 高级片段、全量 JSON | 编辑原始引用。Unity 视图中的相机设置属于工作区状态，独立于这个字段。 |

`runtimeData` 在 2.7.81 标为 `[NonSerialized]`。历史 JSON 如果带有该键，会作为导入原文保留，不为新地图生成。字段树也展示其他未知键。新建会带上 15 字段的 `options`、一个共享禁入区配置、地图索引矩阵和若干编辑所需空集合。导入仍保留未提供、显式 `null`、空值和已有未知字段。

## Options

全局设置第一项编辑 `levelId`，下方独立表单编辑 `options`。下表默认值对应新建文档，推荐候选来自固定样例索引。出处详情按关卡分组，关卡下列出关联 LevelData，未匹配关卡的正文保留自身出处信息。

| 原生字段 | 类型 | 新建值 | 当前编辑与校验 |
| --- | --- | --- | --- |
| `characterLimit` | Int32 | 99 | 部署数，整数控件及原版取值。 |
| `maxLifePoint` | Int32 | 15 | 初始生命，整数控件及非正值行为提示。 |
| `initialCost` | Int32 | 3 | 初始费用，整数控件及原版取值。 |
| `maxCost` | Int32 | 99 | 费用上限，整数控件及原版取值。 |
| `costIncreaseTime` | Single | 1.0 | 自然回费周期，有限数值输入，提供 FP 阈值与转换边界提示。 |
| `moveMultiplier` | Single | 0.5 | 移动倍率，有限数值输入，提供已核实消费者的范围提示。 |
| `steeringEnabled` | Boolean | true | 转向开关。 |
| `reachableCheckIgnoreStartTile` | Boolean | false | 可达检查是否忽略起点格。 |
| `isTrainingLevel` | Boolean | false | 教学关标记。 |
| `isHardTrainingLevel` | Boolean | false | 困难教学标记。 |
| `isPredefinedCardsSelectable` | Boolean | false | 预设卡牌可选标记，关联现有预设输入。 |
| `displayRestTime` | Boolean | false | 剩余时间的显示开关。 |
| `maxPlayTime` | Single | -1.0 | 最大游玩时间参数，提供 FP 与时间相关提示。 |
| `functionDisableMask` | 枚举位掩码 | 0 | 19 个已知位可勾选，保留原生数值或字符串表示及扩展值。 |
| `configBlackBoard` | `List<Blackboard.DataPair>` | null | 内嵌逐键编辑、键样例和 JSON。逐键消费者证据以独立调查为准。 |

`enemyTauntLevelPow`、`deployCostPostDelta`、`deployCostPostDeltaMinCost` 在当前 dump 属于运行字段。历史正文出现这些键时可以原样回写。新增值的结构校验、既有样例、运行消费者提示分别来自类型声明、语料和源码证据。

## MapData、TileData 与预设

| `mapData` 字段 | 已落地 | 下一步边界 |
| --- | --- | --- |
| `map` | 地图绘制、置空、长宽调整与裁剪预检、索引矩阵 JSON。尺寸从矩阵取得。 | 继续沿用坐标与引用检查。历史 `width`／`height` 只作为已导入键保留。 |
| `tiles` | 列表与块状视图、配置增删改查、按坐标多格绑定、共享修改、复制为独立配置。自动置换与清理默认开启，事务提供确定、取消、替换并保留旧配置。 | 扩展有证据的地块黑板控件和跨字段条件。 |
| `blockEdges` | 坐标、方向、移动掩码表单；格边点选/拖动、叠加显示、撤销与裁剪检查。保留原始 JSON 和未知字段。 | 原生共享边双向生效，NONE/WALK_ONLY/FLY_ONLY/ALL 分别显示；路线寻路预览尚未模拟阻挡边。 |
| `tags` | 专用片段，区分缺失、null 和空列表。 | 标签选择与用途校验待做。 |
| `effects` | 专用片段。 | 逐类型效果控件和通用美术预览待做。 |
| `layerRects` | 专用片段与空间提示。 | 区域绘制、逐项属性编辑与表现待做。 |

地图单元格保存 `tiles[]` 的索引。多格绑定同一索引时共享同一配置，分开编辑时复制并改绑。TileData 面板按 `tileKey`、`heightType`、`buildableType`、`passableMask`、`playerSideMask`、`advancedBuildableMask`、`blackboard`、`effects` 展示。其中 `advancedBuildableMask` 属于运行字段。普通枚举有控件，黑板已知键按证据提供编辑，完整 JSON 可处理其他键和效果。预制体和 Tile 的运行态成员通过资源与消费者关联，场景中的预览网格不会替代保存的 TileData。

| 预设数组 | 两个预设层均支持的编辑方式 |
| --- | --- |
| `characterInsts` | 干员、练度与坐标朝向，可从资源库或编队复制生成草稿。地图放置支持只加场上、只加卡牌、两者都加或取消。 |
| `characterCards` | 干员卡牌与练度，按原字段保存技能和模组。手牌区可预览和定位记录。 |
| `tokenInsts` | 装置、坐标朝向和实例记录。原版样例去重忽略位置与朝向，放置时绑定，出处保留原值。 |
| `tokenCards` | 装置卡牌和初始数量。按完整配置去重，手牌区可预览。 |

装置和干员详情均可查看原版配置参数，编辑在左侧。预设动作引用同时涉及波次、分支和别名，当前保留原数据并对已知依赖给出检查，待做可浏览的跨模块引用。

## 编辑缺口优先级

1. 在已落地的路线、波次、分支和共享动作表单上扩充可解析的跨模块引用。路线重排保留历史空槽，演出与玩法引用按各自的消费入口检查。来源见 [Waves 专项](波次与分支数据及编辑设计.md)。
2. 复用已完成的禁放格和阻挡边交互，为 `mapData.tags/effects/layerRects` 补逐项编辑。图层裁剪和资源选择规则见 [全字段复核](LevelData全字段复核.md#地图分层标签与效果)。
3. 分别补 `runes`、`optionalRunes`、`globalBuffs` 的定义目录、输入表单与本地校验。
4. 将 `mapId` 与成品地图的来源版本接到美术资源解析，继续沿用独立的 `.arkmap` 工程映射。音频和相机字段可先接入有出处的引用选择器。

## 核对入口

- 原生声明 `ark-mix-source/DumpCS/2.7.81/dump-pc-x64-source.cs`，LevelData 约第 102722 行，Options 约第 102772 行，PredefinedData 约第 103145 行，TileData 与 MapData 约第 103353–103417 行。
- Unity 包内 `Editor/Core/LevelDocument.cs`、`LevelDocument.InputCommands.cs`、`LevelDocument.MapData.cs`。UI 对照 `Editor/UI/MapEditorWindow.Workbench.cs`、`MapEditorWindow.Editing.cs` 和相应模块文件。
- [MapData 设计](地图数据编辑设计.md)、[Options 设计](全局设置编辑设计.md)、[Options 值域](全局设置值域研究.md)、[Options 黑板](全局设置黑板逐键约束.md)。
- [逐字段进度树](LevelData编辑进度.md)、[Unity 主实施计划](PlanDocumentsForImplementation/UnityEditor/Unity编辑器主实施计划.md)。
- [全字段复核](LevelData全字段复核.md) 与 [字段及类型附录](LevelData原生字段与类型.md) 提供原生读取、正文覆盖和实现边界。

## Waves 后续设计（2026-11-14）

动作字段适用性及引用见 [Waves 专项](波次与分支数据及编辑设计.md#动作枚举与引用)。主波次结构化表单已接入本关敌人与主路线。原生调度会给 `SPAWN` 与 `PREVIEW_CURSOR` 写入运行时 `useExtraRoute`，`PLAY_OPERA` 的部分节点读取收到的原值。当前预设删除采用粗粒度保护。5-10 的外部技能引用及 AW-6 的关卡内分支键和动作索引已核实，精确引用保护待接入。

原生出怪从已加载的本关敌人表查找，`useDb=false` 可定义独立敌人。敌人表构成和同键优先级见 [敌人资源库设计](敌人资源库数据设计.md)。

## 符文与增益研究设计（2026-11-14）

三个根字段的原生结构、全量配置、资源库和编辑方案已单独完成，见[专项设计](符文与增益数据及编辑设计.md)。本轮未改插件，表内已落地状态保持摘要和 JSON 编辑；专用表单、目录与关联规则按专项计划推进。
