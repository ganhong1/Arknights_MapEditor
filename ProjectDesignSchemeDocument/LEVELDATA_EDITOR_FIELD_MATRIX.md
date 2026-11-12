# LevelData 字段与编辑器落地复盘

复核日期 2026-11-12。原生结构以 PC 2.7.81 的 `Torappu.Battle.LevelData`（TypeDefIndex 4680）及其关联类型为准。编辑实现以 Unity 功能工作树为准。成品库的历史正文和 2.7.81 原生字段基线各有独立来源。

## 顶层字段

按原生声明顺序列出 22 个可序列化字段。其中 6 个有结构化编辑模块，2 个有部分结构化操作，8 个有独立或高级 JSON 片段入口，6 个目前通过全量 JSON 编辑。中间字段树展示已应用文档，实际编辑入口在左侧模块或「原始 JSON」。全量 JSON 应用仍经过文档解析和核心检查，资源引用和实际游戏效果另由相应程序验证。

| 字段 | 原生类型或形态 | 当前入口 | 已落地的能力与边界 |
| --- | --- | --- | --- |
| `options` | `Options` | 全局设置 | 15 字段表单按原序排列，支持构造默认对象、样例值与出处、自定义值、缺失与 null，保留原始 JSON。详见下表。 |
| `levelId` | `string` | 全量 JSON | Core 有 `WithLevelId`，当前无独立控件。成品库的关卡 ID、模式、版本由索引提供。 |
| `mapId` | `string` | 高级片段、全量 JSON | 可编辑原始值，当前没有把它接到通用 Unity 场景解析或美术选择器。 |
| `bgmEvent` | `string` | 背景音乐 | 专用 JSON 片段、应用、重读。曲目事件字典、资源校验与试听待接。 |
| `environmentSe` | `string` | 环境音效 | 专用 JSON 片段、应用、重读。环境音效事件字典、资源校验与试听待接。 |
| `mapData` | `MapData` | 地图数据 | 六字段分栏，网格与共享 TileData 可结构化编辑，其余四项支持各自的 JSON 片段。详见下表。 |
| `tilesDisallowToLocate` | `List<GridPosition>` | 全量 JSON | Core 裁剪预检会检查已有坐标。待做禁止落点的坐标列表、地图拾取与叠加标记。 |
| `runes` | `LegacyInLevelRuneData[]` | 符文与增益、高级片段 | 左侧模块摘要与字段树定位，编辑用高级 JSON。待做原版定义、引用和黑板编辑。 |
| `optionalRunes` | `Dictionary<string,List<LegacyInLevelRuneData>>` | 符文与增益、高级片段 | 保留键分组及原文状态。待做分组管理、条件与引用检查。 |
| `globalBuffs` | `GlobalBuffData[]` | 符文与增益、高级片段 | 摘要与 JSON 片段可用。待做逐项效果和参数表单。 |
| `routes` | `RouteData[]` | 路线、高级片段 | 起终点控件、检查点与路线 JSON、路线增删重排、已知引用更新和 PRTSMap 式演示。检查点尚无完整逐类型表单。 |
| `extraRoutes` | `RouteData[]` | 路线、高级片段 | 与主路线分别存放和选择，动作引用按分支与 `useExtraRoute` 解析。 |
| `enemies` | `EnemyData[]` | 全量 JSON | 直接内嵌的敌人正文，现有「敌人定义」按钮定位 `enemyDbRefs`。待做两类来源的明确切换。 |
| `enemyDbRefs` | `EnemyDataDbReference[]` | 敌人定义、高级片段 | 模块摘要、JSON 片段、已知引用保护删除。静态检查覆盖 ID 类型、重复 ID 和 SPAWN 引用；独立选择与覆盖参数表单待接。 |
| `waves` | `WaveData[]` | 波次、高级片段 | 可建主波次及初始 Fragment，按索引读取、插入、删除、移动 Action。动作参数以 JSON 编辑，Fragment 层管理和可视时间线待做。 |
| `branches` | `ListDict<string, BranchData>` | 分支、波次、高级片段 | 分支页是摘要。波次页可按 key 建分支及首个 Phase，再编辑该 Phase 的 Action。独立分支管理、Phase 表单、预设别名关联待做。 |
| `predefines` | `PredefinedData` | 关卡预设 | 基础预设，四类记录分开编辑，支持场上、卡牌、资源库选用、编队复制和手牌预览。 |
| `hardPredefines` | `PredefinedData` | 关卡预设 | 困难预设使用同一套控件但独立读写。选择预设层只改变编辑和示意对象，游戏实际采用哪层由关卡运行上下文决定。 |
| `excludeCharIdList` | `string[]` | 全量 JSON | 保留已有字段。待做以干员 ID 为值的多选与重复检测。 |
| `randomSeed` | `int` | 全量 JSON | 可随正文保存，当前无独立 Int32 控件和编辑器内的随机结果回放。 |
| `operaConfig` | `string` | 全量 JSON | 保留原值，当前无演出引用表单和验证器。 |
| `cameraPlugin` | `string` | 高级片段、全量 JSON | 编辑原始引用。Unity 视图中的相机设置属于工作区状态，独立于这个字段。 |

`runtimeData` 在 2.7.81 标为 `[NonSerialized]`。历史 JSON 如果带有该键，会作为导入原文保留，不为新地图生成。字段树也展示其他未知键。新建会带上 15 字段的 `options`、一个共享禁入区配置、地图索引矩阵和若干编辑所需空集合。导入仍保留未提供、显式 `null`、空值和已有未知字段。

## Options

全局设置只编辑 `options`。下表默认值对应新建文档，推荐候选来自固定样例索引。出处详情按关卡分组，关卡下列出关联 LevelData，未匹配关卡的正文保留自身出处信息。

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
| `blockEdges` | 专用片段与空间检查，保留 `pos/direction/blockMask`。 | 边界图层和图形编辑待做。 |
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

1. 完成波次／分支与敌人定义的结构化编辑和引用跳转，优先连接动作、路线、敌人引用和预设别名。
2. 给禁放格和阻挡边接入地图交互，复用现有坐标与裁剪检查。
3. 分别补 `runes`、`optionalRunes`、`globalBuffs` 的定义目录、输入表单与本地校验。
4. 将 `mapId` 与成品地图的来源版本接到美术资源解析，继续沿用独立的 `.arkmap` 工程映射。音频和相机字段可先接入有出处的引用选择器。

## 核对入口

- 原生声明 `ark-mix-source/DumpCS/2.7.81/dump-pc-x64-source.cs`，LevelData 约第 102722 行，Options 约第 102772 行，PredefinedData 约第 103145 行，TileData 与 MapData 约第 103353–103417 行。
- Unity 包内 `Editor/Core/LevelDocument.cs`、`LevelDocument.InputCommands.cs`、`LevelDocument.MapData.cs`。UI 对照 `Editor/UI/MapEditorWindow.Workbench.cs`、`MapEditorWindow.Editing.cs` 和相应模块文件。
- [MapData 设计](MAPDATA_EDITOR_DESIGN.md)、[Options 设计](OPTIONS_EDITOR_DESIGN.md)、[Options 值域](OPTIONS_VALUE_RESEARCH_20261106.md)、[Options 黑板](OPTIONS_BLACKBOARD_CONSTRAINTS_20261106.md)。
- [当前代码与提交批次](LOCAL_COMMIT_REVIEW_20261112.md)、[Unity 主实施计划](PlanDocumentsForImplementation/UnityEditor/UNITY_EDITOR_IMPLEMENTATION_PLAN.md)。
