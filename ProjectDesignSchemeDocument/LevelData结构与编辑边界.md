# LevelData 结构与编辑边界

整理日期 2026-11-06。敌人结构、原生读取入口和版本沿革更新于 2026-11-13。共用动作结构与引用更新于 2026-11-14。

依据：PC 2.7.81 的 `Torappu.LevelData` 类型声明。来源为 APW 已有 [dump-pc-x64-inspector-20261026.cs](../../ark-mix-source/DumpCS/2.7.81/dump-pc-x64-inspector-20261026.cs)，LevelData 声明从第 640059 行开始。

**本文完整列出该版本的顶层字段，并展开主要编辑结构。复杂子类型使用类型名标注，不代表其内部只有这些内容，也不是所有嵌套类型的完整 Schema。** 类型声明可以证明字段与继承关系，不能单独证明构造器默认值、所有黑板键或运行效果。

[全字段复核](LevelData全字段复核.md) 记录了 8,664 份代表正文和低频字段的原生消费链。逐类型的完整嵌套字段、枚举及二进制偏移见 [字段与类型附录](LevelData原生字段与类型.md)。

## 1. LevelData 总树

```text
LevelData
│
├─ options                         关卡规则配置
│  ├─ characterLimit               部署数量限制
│  ├─ maxLifePoint                 最大生命点
│  ├─ initialCost                  初始费用
│  ├─ maxCost                      费用上限
│  ├─ costIncreaseTime             费用增长时间参数
│  ├─ moveMultiplier               移动倍率
│  ├─ steeringEnabled              转向相关开关
│  ├─ reachableCheckIgnoreStartTile 可达性检查是否忽略起始格
│  ├─ isTrainingLevel              教学关标记
│  ├─ isHardTrainingLevel          困难教学关标记
│  ├─ isPredefinedCardsSelectable   预设卡牌是否允许选择
│  ├─ displayRestTime              是否显示剩余时间
│  ├─ maxPlayTime                  最大游玩时间
│  ├─ functionDisableMask          战斗功能禁用掩码
│  └─ configBlackBoard             关卡配置黑板
│
├─ levelId                         关卡资源标识字段
├─ mapId                           地图场景关联标识
├─ bgmEvent                        背景音乐事件
├─ environmentSe                   环境音配置
│
├─ mapData                         地图布局与地块配置
│  ├─ map[row][col]                每格存放 tiles[] 的索引
│  ├─ tiles[]                      TileData 配置表
│  │  ├─ tileKey                   地块资源键
│  │  ├─ heightType                高度类型
│  │  ├─ buildableType             部署类型
│  │  ├─ passableMask              通行掩码
│  │  ├─ playerSideMask            阵营掩码
│  │  ├─ blackboard                该配置的黑板
│  │  └─ effects[]                 地块效果配置，复用 MapEffectData
│  ├─ blockEdges[]                 格子边界阻挡
│  │  ├─ pos                       格子坐标
│  │  ├─ direction                 边界方向
│  │  └─ blockMask                 阻挡运动类型
│  ├─ tags[]                       地图标签
│  ├─ effects[]                    地图效果配置
│  │  ├─ key                      资源键
│  │  ├─ offset                   Vector3，x/y/z
│  │  └─ direction                UP/RIGHT/DOWN/LEFT
│  └─ layerRects[]                 分层区域配置
│
├─ tilesDisallowToLocate[]         禁止部署格；初始化覆盖运行 Tile.buildableType 为 NONE，不修改原 TileData
│
├─ runes[]                        LegacyInLevelRuneData：关卡符文
├─ optionalRunes                  按字符串键组织的可选符文列表
├─ globalBuffs[]                  GlobalBuffData：全局效果配置
│
├─ routes[]                       主路线
│  ├─ motionMode                  运动方式
│  ├─ startPosition               起点
│  ├─ endPosition                 终点
│  ├─ spawnRandomRange            出生位置随机范围
│  ├─ spawnOffset                 出生偏移
│  ├─ checkpoints[]               检查点
│  │  ├─ type                     检查点类型
│  │  ├─ time                     时间参数
│  │  ├─ position                 格子位置
│  │  ├─ reachOffset              到达偏移
│  │  ├─ randomizeReachOffset     是否随机化到达偏移
│  │  └─ reachDistance            到达距离参数
│  ├─ allowDiagonalMove           允许斜向移动
│  ├─ visitEveryTileCenter         经过每个地块中心
│  ├─ visitEveryNodeCenter         经过每个路径节点中心
│  └─ visitEveryCheckPoint         经过每个检查点
│
├─ extraRoutes[]                  额外路线，结构同 routes[]
│
├─ enemies[]                      内嵌 EnemyData 敌人定义
├─ enemyDbRefs[]                  EnemyDataDbReference 敌人数据库引用
│
├─ waves[]                        主波次调度
│  ├─ preDelay                    波次前延时
│  ├─ postDelay                   波次后延时
│  ├─ maxTimeWaitingForNextWave    片段完成后的清场等待上限（末波不采用）
│  ├─ advancedWaveTag             高级波次标签
│  └─ fragments[]                 波次片段
│     ├─ preDelay                 片段前延时
│     └─ actions[]                动作，见第 2 节
│
├─ branches                       按字符串键索引的分支调度
│  └─ 分支键 → BranchData
│     └─ phases[]                 分支阶段
│        ├─ preDelay              阶段前延时
│        └─ actions[]             同一套动作结构
│
├─ predefines                     常规预设输入
│  ├─ characterInsts[]            场上角色预设
│  ├─ tokenInsts[]                场上装置预设
│  ├─ characterCards[]            角色卡牌预设
│  └─ tokenCards[]                装置卡牌预设
│
├─ hardPredefines                 困难预设输入，结构同 predefines
│                                 FOUR_STAR 且为困难训练、困难预设存在时替换干员卡牌
│
├─ excludeCharIdList[]            排除的角色 ID 列表
├─ randomSeed                     正文随机种子字段，实际战斗种子另见调度入口
├─ operaConfig                    演出配置资源键，PLAY_OPERA 再选资源内命令
├─ cameraPlugin                   相机插件关联
│
└─ runtimeData                    【NonSerialized】运行时数据
                                  不作为常规关卡 JSON 编辑内容
```

## 2. 波次与分支共用的动作结构

```text
ActionData
├─ actionType                     动作类型
├─ managedByScheduler             是否受调度器管理
├─ key                            动作目标键，含义由动作类型决定
├─ count                          数量
├─ preDelay                       前延时
├─ interval                       执行间隔
├─ routeIndex                     路线索引
├─ blockFragment                  等待动作完成回调，与等待击杀分开
├─ autoPreviewRoute               自动预览路线
├─ autoDisplayEnemyInfo           自动显示敌人信息
├─ isUnharmfulAndAlwaysCountAsKilled
├─ hiddenGroup                    隐藏组
├─ randomSpawnGroupKey            随机生成组键
├─ randomSpawnGroupPackKey        随机生成组包键
├─ randomType                     随机类型
├─ refreshType                    刷新类型
├─ weight                         权重
├─ dontBlockWave                  不阻塞波次
├─ forceBlockWaveInBranch         允许分支生成的敌人阻挡波次结束
├─ [NonSerialized] useExtraRoute  本次使用额外路线库
├─ [NonSerialized] isValid        动作运行时有效性
├─ [NonSerialized] notCountInTotal 动作级敌人总数计数标记
├─ [NonSerialized] extraMeta      运行时附加数据
├─ [NonSerialized] actionId       LevelData.ActionID
│  ├─ subSchedulerI
│  ├─ waveI
│  ├─ fragI
│  └─ actionI
└─ [JsonIgnore] weightValue       weight 转 float 的只读属性
```

主波次按 Wave → Fragment → Action 三层组织。Wave 管前后延时和清场，Fragment 管一组动作的共同时间起点，Action 管具体操作及目标。`blockFragment` 等待动作完成，波次清场检查当前波参与阻塞的管理敌人。5-10 只有一波、十个片段，首片段包含敌情展示和走 `routes[1]` 的出怪配置。

`actionType` 决定动作参数。`SPAWN` 同时引用本关敌人和路线，`PREVIEW_CURSOR` 只引用路线，路线库由主波次／分支上下文决定。预设激活和撤回读取别名，触发动作再解析挂载能力。`PLAY_OPERA` 通过 `operaConfig` 取得演出资源，部分节点还按 `routeIndex` 和运行时 `useExtraRoute` 取路线。`BATTLE_EVENTS` 交给当前玩法或关卡监听器。`EMPTY` 是随机候选占位，`E_NUM` 是枚举末尾标记。字段类型与 15 个枚举的逐项读取见 [Waves 专项方案](波次与分支数据及编辑设计.md#动作枚举与引用)。

## 3. 角色与装置预设

```text
predefines / hardPredefines
│
├─ characterInsts[] / tokenInsts[]
│  └─ PredefinedCharacter
│     ├─ 共同角色输入 → 见下方继承树
│     ├─ hidden
│     ├─ alias
│     ├─ position
│     └─ direction
│
├─ characterCards[]
│  └─ PredefinedCard
│     ├─ 共同角色输入
│     ├─ hidden
│     └─ alias
│
└─ tokenCards[]
   └─ PredefinedTokenCard
      ├─ 共同角色输入
      ├─ hidden
      ├─ alias
      └─ initialCnt
```

共同字段的继承关系：

```text
CharacterInst
├─ inst
│  ├─ characterKey
│  ├─ level
│  ├─ phase
│  ├─ favorPoint                 JSON 名；声明为 favorBattlePhase
│  └─ potentialRank
├─ skillIndex
├─ mainSkillLvl
├─ skinId
├─ tmplId
├─ overrideSkillBlackboard
├─ overrideTalents[]
│  ├─ prefabKey
│  └─ blackboard
│
└─ AdvancedCharacterInst
   ├─ uniEquipIds
   ├─ showSpIllust
   ├─ masterInfos
   │
   └─ PredefinedInst
      ├─ hidden
      ├─ alias
      ├─ PredefinedCharacter
      │  ├─ position
      │  └─ direction
      └─ PredefinedCard
         └─ PredefinedTokenCard
            └─ initialCnt
```

装置预设是本局输入，不是装置预制体的完整序列化内容。装置的静态属性、技能、天赋和预制体组件仍来自其他定义与资源。

## 4. 不作为常规输入编辑的运行时字段

该版本声明中，下列字段带有 `NonSerialized`：

| 所在结构               | 字段                                                                |
| ---------------------- | ------------------------------------------------------------------- |
| LevelData              | runtimeData                                                         |
| Options                | enemyTauntLevelPow、deployCostPostDelta、deployCostPostDeltaMinCost |
| ActionData             | useExtraRoute、isValid、notCountInTotal、extraMeta、actionId        |
| CharacterInst.Metadata | playerInstId，同时有 JsonIgnore                                     |

以上是已展开结构中的相关字段，不是整个游戏类型系统的完整运行时字段清单。MapData 的 width/height 是带 JsonIgnore 的计算属性，布局大小由 map 数组决定。

## 5. 编辑器的责任范围

- 生产 LevelData，保留未知字段与缺失／null／空数组的区别，并检查已知内部引用。
- 地块坐标通过 `map[row][col] → tiles[index]` 关联；TileData 自身不携带格子坐标。
- 场上装置通过其预设记录的 position/direction 表达摆放；卡牌不是已放置装置。
- 预制体、模型、材质、技能实现等外部资源不内嵌在 LevelData 中。
- 模式可能选择不同 LevelData，也可能对输入做处理；不能仅凭 hardPredefines 等字段名推导所有模式行为。
- 本文不是全量嵌套 Schema 或默认值清单。黑板可用键、构造器默认值及具体效果需要分别核实。


## 6. 敌人定义、资源库与调度的完整关系

以下原生结构依据PC 2.7.81声明及固定DLL方法体核实；资源库组织方案已获用户认可。资源库、当前地图定义、出怪动作分别承担不同职责。

```text
EnemyDatabase（关卡外的原版数据库）
└─ enemies: enemyId → EnemyLevel[]
   ├─ level
   └─ enemyData: EnemyDatabase.EnemyData
      ├─ name / description / prefabKey
      ├─ attributes
      ├─ applyWay / motion / enemyTags
      ├─ lifePointReduce / levelType / rangeRadius
      ├─ numOfExtraDrops / viewRadius / notCountInTotal
      ├─ talentBlackboard
      ├─ skills[]
      │  ├─ prefabKey / priority / cooldown / initCooldown / spCost
      │  └─ blackboard
      └─ spData: spType / maxSp / initSp / increment

LevelData（当前地图）
├─ enemyDbRefs[]: EnemyDataDbReference
│  ├─ useDb: bool
│  ├─ id: string
│  ├─ level: int
│  └─ overwrittenData: EnemyDatabase.EnemyData
├─ enemies[]: LevelData.EnemyData
│  ├─ name / description / key / alias
│  ├─ attributes / applyWay / motion / enemyTags
│  ├─ notCountInTotal / lifePointReduce / rangeRadius
│  ├─ numOfExtraDrops / viewRadius / levelType
│  └─ talentBlackboard / skills[] / spData
├─ routes[] / extraRoutes[]: RouteData
│  └─ motionMode / 起终点 / 检查点 / 路线参数
├─ waves[].fragments[].actions[]
│  └─ SPAWN: key → 本关敌人身份，routeIndex → routes[]
└─ branches[分支key].phases[].actions[]
   └─ SPAWN: key → 本关敌人身份，routeIndex → extraRoutes[]
```

EnemyDatabase.EnemyData为稀疏定义/补丁型，多数普通字段及属性使用Undefinable<T>{m_defined,m_value}；LevelData.EnemyData为实际值型。前者prefabKey映射到后者key。talentBlackboard、skills、spData使用各自合成规则，不是统一的逐字段Undefinable。

| 定义方式 | 原生处理 | 编辑器操作 |
| --- | --- | --- |
| useDb=true | 按id/level读取并合成数据库定义，再应用overwrittenData | 添加为数据库引用 |
| `enemyDbRefs[].useDb=false` | 新建 `EnemyData`，从构造默认值应用 `overwrittenData`，非空 `id` 用作本关引用键，留空时使用预制体键 | 复制为独立定义 |
| `enemies[]` | 直接保存实际值型 `EnemyData`，以非空 `alias` 或预制体 `key` 确定本关引用键 | 新建或复制为内联定义 |

独立定义的id非空且与解析key不同时，原生设alias=id。keyInLevel是计算属性：alias非空用alias，否则用key，不在引用对象上新增alias字段。独立变体从原版复制时应先解析完整基础定义，不能只复制HP差量并期待其余继续从数据库继承。同prefabKey、不同本关id可以组织不同属性变体。

常规出怪与局内敌方情报共用调度器的敌人表。成功解析的 `enemyDbRefs[]` 先登记，`enemies[]` 补入尚未占用的本关引用键，模式可再追加定义。

同键优先级取决于读取入口。资源预加载可为两份同键定义分别建立任务。ArkDex 的攻击方式查询先查内联，再查引用与模式追加定义。自走棋的强度估算优先读内联数值。六张原版地图的逐份核对与 2019–2026 年版本证据见 [敌人专项复核](敌人资源库数据设计.md#内联敌人的读取入口同键优先级与版本沿革)。

### 6.1 特殊技能、天赋和机制

```text
敌人定义
├─ prefabKey/key → 预制体、行为组件和代码实现
├─ talentBlackboard → 对应实现读取的机制参数
└─ skills[].prefabKey＋blackboard → 技能实现与参数
   └─ 具体实现可能关联
      ├─ branches
      ├─ 其他敌人身份
      ├─ routes / extraRoutes
      ├─ predefines / hardPredefines中的alias
      └─ 地图及模式运行输入
```

原生没有统一dependencies数组。关联要求由编辑器依据配置和已核实代码整理，区分已确认依赖与样例共现。它不写入LevelData。复制敌人定义不自动导入整份原版关卡或全部依赖。

### 6.2 已认可的资源库结构

```text
EnemyCatalogEntry（编辑器内部，不是LevelData新增字段）
├─ 固定来源版本＋enemyId
├─ 基本信息：名称、头像、分类、标签及来源
├─ levels[]：全部原始EnemyLevel记录及只读合成预览
├─ 默认推荐（最多三个）
├─ 全部既有已知配置
│  ├─ enemyDbRefs完整配置样例
│  └─ inline enemies样例，单独分类
├─ 当前配置出处／总出处：关卡代号、名称、模式
└─ 已核实关联要求
```

一敌人一个资源条目，不按等级复制卡片。右侧负责搜索、标签筛选、选中、详情和选用；字段编辑在左侧“敌人定义”。左侧按原四字段组织引用/独立定义，显示继承、覆盖及解析结果，并列出波次/分支使用位置。技能等复合字段按原生合成规则编辑。

左侧「本关已定义敌人」的左栏显示 `enemyDbRefs[]`，右栏显示 `enemies[]`。两栏的卡片打开同一个编辑区，每条记录和两个新增入口各自保留草稿。内联记录直接编辑实际值，`key` 为预制体键，`alias` 非空时作为本关引用键。引用技能可从继承值创建完整覆盖，技力覆盖以完整对象提交。天赋和技能黑板保留有序数组、原始值与未知字段。

路线几何由 `routes[]` 和 `extraRoutes[]` 持有。`SPAWN` 同时保存敌人 `key` 与 `routeIndex`，以及数量、时序、随机组、权重和阻塞字段。敌人定义和路线都可以独立创建，出场动作负责选择两者。AW-6 的历史 GameData 正文中，分支动作使用同一个 `enemy_1255_lybgpa_2` 键，分别引用 `extraRoutes[0]` 至 `extraRoutes[11]`。常规调度选择库的规则见 [Waves 专项](波次与分支数据及编辑设计.md#路线)，编辑操作见 [场景路线编辑](Unity编辑器总体设计.md#场景路线编辑设计)。

选用敌人时只建立或复用本关定义。敌人定义更名或删除前先展示按动作类型解析的引用影响。

详细字段、合成规则、版本来源及语料统计见[敌人资源库设计](敌人资源库数据设计.md)；调度语义见[Waves专项](波次与分支数据及编辑设计.md)。上述资源库设计已认可，具体实现进度以[字段落地矩阵](LevelData字段与编辑能力核对.md)和实施计划为准。

### branches 编辑落地（2026-11-14）

左侧「分支」直接对应 `branches[branchKey].phases[phaseIndex].actions[actionIndex]`。BranchKey 重命名需核对调用引用，阶段次序影响顺序调用，动作索引可能由技能黑板 `action_index` 读取。分支 SPAWN / PREVIEW_CURSOR 使用 `extraRoutes`；EMPTY 可由分支路线查询消费，保留原有哨兵与参数。预设动作按有效 alias 选择，卡牌与场上记录、基础与困难层均标注所在位置，运行生效层仍由原生加载选择。

本关已核实的 AW-6 装置黑板可随分支改名同步。5-10 浮士德技能中的外部键、未知同值字符串不会自动改写，需先处理引用。局部动作草稿独立于文档，应用后写回原字段；动作新增先选目标，不自动创建敌人、路线或预设。实现及回归见 WAVES_STRUCTURE_AND_EDITOR_DESIGN.md。

## 符文与全局效果的数据及编辑设计（2026-11-14）

`runes` 与 `optionalRunes` 的条目都是 LegacyInLevelRuneData，按原生顺序为 difficultyMask、key、professionMask、buildableMask、blackboard。blackboard 为有序 DataPair 列表（key/value/valueStr）。`globalBuffs` 的持久字段为 prefabKey、blackboard、overrideCameraEffect、passProfessionMaskFlag、professionMask、playerSideMask；其 useExtraData/extraRuntimeData/source 是当前原生非序列化字段，历史正文出现时仍保留。

现代 RuneData.Selector 在原生转换和黑板补充阶段形成，不直接作为 Legacy 条目的外层字段导出。完整字段树、加载关系、8,664 份语料和三页签编辑方案见[专项设计](符文与增益数据及编辑设计.md)。当前插件仍为摘要/JSON 入口。
