# LevelData 编辑进度

```text
LevelData（PC 2.7.81）
├─ [√完全实现] options                               全局设置，按原生顺序编辑
│  ├─ [√完全实现] characterLimit                      部署数量，默认 99
│  ├─ [√完全实现] maxLifePoint                        生命参数，默认 15
│  ├─ [√完全实现] initialCost                         初始费用，默认 3
│  ├─ [√完全实现] maxCost                             费用上限，默认 99
│  ├─ [√完全实现] costIncreaseTime                    自然回费周期，默认 1.0
│  ├─ [√完全实现] moveMultiplier                      移动倍率，默认 0.5
│  ├─ [√完全实现] steeringEnabled                     转向，默认 true
│  ├─ [√完全实现] reachableCheckIgnoreStartTile        可达性检查忽略起点，默认 false
│  ├─ [√完全实现] isTrainingLevel                     教学关，默认 false
│  ├─ [√完全实现] isHardTrainingLevel                 困难教学，默认 false
│  ├─ [√完全实现] isPredefinedCardsSelectable          预设卡牌可选，默认 false
│  ├─ [√完全实现] displayRestTime                     显示剩余时间，默认 false
│  ├─ [√完全实现] maxPlayTime                         最大游玩时间参数，默认 -1.0
│  ├─ [√完全实现] functionDisableMask                  19 个已知禁用位，默认 0
│  └─ [√完全实现] configBlackBoard[]                   支持已知键和样例，默认 null
│     ├─ [√完全实现] key                              参数键
│     ├─ [√完全实现] value                            数值
│     └─ [√完全实现] valueStr                         字符串值
├─ [√完全实现] levelId                               全局设置第一项，成品地图副本带入关卡 ID
├─ [×还未实现or占位] mapId                            高级 JSON 可写，尚无地图资源选择器
├─ [√完全实现] bgmEvent                              开局音乐，输入事件键或从音乐库选用
├─ [=部分实现] environmentSe                         音效库选用，手动输入走字段 JSON
├─ [=部分实现] mapData
│  ├─ [√完全实现] map[row][col]                       每格存 tiles[] 索引，支持绘制、置空和裁剪
│  ├─ [=部分实现] tiles[] → TileData                  列表、块状、增删改查、多格共享绑定
│  │  ├─ [=部分实现] tileKey                          从地形库选用，现有配置键在表单中只读
│  │  ├─ [√完全实现] heightType                       地面／高台
│  │  ├─ [√完全实现] buildableType                    部署类型
│  │  ├─ [√完全实现] passableMask                     地面／飞行通行掩码
│  │  ├─ [√完全实现] playerSideMask                   玩家侧
│  │  ├─ [=部分实现] blackboard[]{key,value,valueStr}  已核实键有表单，其余通过配置 JSON
│  │  ├─ [×还未实现or占位] effects[]                  地块特效，通过配置 JSON
│  │  │  └─ key / offset{x,y,z} / direction
│  │  └─ advancedBuildableMask                        运行时字段，面板显示
│  ├─ [=部分实现] blockEdges[]                        可用表单和 SceneView 编辑，路线预览未计算阻挡
│  │  ├─ [√完全实现] pos{row,col}                     所属格
│  │  ├─ [√完全实现] direction                       格边方向
│  │  └─ [√完全实现] blockMask                       阻挡的移动类型
│  ├─ [×还未实现or占位] tags[]                        字段 JSON，尚无标签选择和逐项编辑
│  ├─ [×还未实现or占位] effects[]                     字段 JSON，尚无地图特效表单和资源绑定
│  │  └─ key / offset{x,y,z} / direction
│  └─ [×还未实现or占位] layerRects[]                  字段 JSON，尚无 A/B 图层区域绘制
├─ [√完全实现] tilesDisallowToLocate[]                禁止部署格，可点选、拖动及框选
│  ├─ [√完全实现] row                                行
│  └─ [√完全实现] col                                列
├─ [×还未实现or占位] runes[]                          符文与增益模块摘要，高级 JSON
│  └─ LegacyInLevelRuneData
│     ├─ difficultyMask                               适用难度
│     ├─ key                                          符文键
│     ├─ professionMask                               职业条件
│     ├─ buildableMask                                部署类型条件
│     └─ blackboard[]{key,value,valueStr}             符文参数
├─ [×还未实现or占位] optionalRunes                    可选符文，摘要和高级 JSON
│  └─ <分组键>[] → LegacyInLevelRuneData
├─ [×还未实现or占位] globalBuffs[]                    全局增益，摘要和高级 JSON
│  ├─ prefabKey                                       增益预制体键
│  ├─ blackboard[]{key,value,valueStr}                 增益参数
│  ├─ overrideCameraEffect                            镜头效果引用
│  ├─ passProfessionMaskFlag                          职业条件标记
│  ├─ professionMask                                  职业掩码
│  └─ playerSideMask                                  玩家侧掩码
├─ [√完全实现] routes[] → RouteData                   主路线，可编辑、重排、场景绘制及演示
├─ [√完全实现] extraRoutes[] → RouteData              额外路线，复用表单并独立保存
├─ [=部分实现] enemies[]                             关卡内联敌人，支持增删改、撤销和原生 JSON
│  ├─ [=部分实现] key                                原生对象键，表单展示
│  ├─ [√完全实现] alias                              关卡引用别名
│  ├─ [√完全实现] name / description                  名称、描述
│  ├─ [√完全实现] applyWay / motion                   应用方式、行动方式
│  ├─ [√完全实现] enemyTags[] / levelType             敌人标签、地位
│  ├─ [√完全实现] notCountInTotal / lifePointReduce   计数、扣除生命
│  ├─ [√完全实现] rangeRadius / viewRadius            攻击范围、视野半径
│  ├─ [√完全实现] numOfExtraDrops                     额外掉落数量
│  ├─ [√完全实现] attributes                          生命、攻防、速度、阻挡、免疫等
│  ├─ [√完全实现] talentBlackboard[] → BlackboardPair
│  ├─ [√完全实现] skills[] → ESkillData
│  └─ [√完全实现] spData → ESpData
├─ [√完全实现] enemyDbRefs[]                         数据库引用或关卡独立定义
│  ├─ [√完全实现] useDb                              true 引用数据库，false 关卡独立定义
│  ├─ [√完全实现] id                                 本关引用 ID
│  ├─ [√完全实现] level                              数据库等级
│  └─ [√完全实现] overwrittenData                    原版覆盖结构，可查看合成结果
│     ├─ [√完全实现] prefabKey / name / description  覆盖值保留 m_defined / m_value
│     ├─ [√完全实现] applyWay / motion / enemyTags / levelType
│     ├─ [√完全实现] lifePointReduce / notCountInTotal / numOfExtraDrops
│     ├─ [√完全实现] rangeRadius / viewRadius
│     ├─ [√完全实现] attributes.<属性>               逐项覆盖，保留 m_defined / m_value
│     ├─ [√完全实现] talentBlackboard[] → BlackboardPair
│     ├─ [√完全实现] skills[] → ESkillData
│     └─ [√完全实现] spData → ESpData
├─ [=部分实现] waves[]
│  ├─ [√完全实现] preDelay                           波次前延时
│  ├─ [√完全实现] postDelay                          波次后延时
│  ├─ [√完全实现] maxTimeWaitingForNextWave           进入下一波的等待上限
│  ├─ [=部分实现] fragments[]
│  │  ├─ [√完全实现] preDelay                        片段前延时
│  │  └─ [=部分实现] actions[] → ActionData           关联覆盖见共用结构
│  └─ [√完全实现] advancedWaveTag                    高级波次标签
├─ [=部分实现] branches
│  └─ [=部分实现] <分支键>                           可新建、重命名、复制、删除
│     └─ [=部分实现] phases[]                        可增删及重排，检查已识别调用
│        ├─ [√完全实现] preDelay                     阶段前延时
│        └─ [=部分实现] actions[] → ActionData        常规出怪选择额外路线
├─ [=部分实现] predefines → PredefinedData            基础预设
├─ [=部分实现] hardPredefines → PredefinedData        困难预设，复用表单并独立保存
├─ [×还未实现or占位] excludeCharIdList[]              整份 JSON，尚无干员多选控件
├─ [×还未实现or占位] randomSeed                       整份 JSON，尚无独立控件和随机结果回放
├─ [=部分实现] operaConfig                            整份 JSON 写入，PLAY_OPERA 读取它筛选命令
├─ [×还未实现or占位] cameraPlugin                     高级 JSON，尚无插件选择和绑定
└─ runtimeData                                        运行时字段，新建地图不生成
```

## RouteData

```text
[√完全实现] RouteData
├─ [√完全实现] motionMode                            地面／飞行
├─ [√完全实现] startPosition{row,col}                起点
├─ [√完全实现] endPosition{row,col}                  终点
├─ [√完全实现] spawnRandomRange{x,y}                 出生随机范围
├─ [√完全实现] spawnOffset{x,y}                      出生偏移
├─ [√完全实现] checkpoints[]                        支持插入、拖动和重排
│  ├─ [√完全实现] type                              移动、等待、消失、出现、巡逻等
│  ├─ [√完全实现] time                              时间参数
│  ├─ [√完全实现] position{row,col}                  坐标
│  ├─ [√完全实现] reachOffset{x,y}                   到达偏移
│  ├─ [√完全实现] randomizeReachOffset               随机化到达偏移
│  └─ [√完全实现] reachDistance                      到达距离
├─ [√完全实现] allowDiagonalMove                     允许斜向移动
├─ [√完全实现] visitEveryTileCenter                  经过每格中心
├─ [√完全实现] visitEveryNodeCenter                  经过每节点中心
└─ [√完全实现] visitEveryCheckPoint                  经过每个检查点
```

## ActionData

```text
[=部分实现] ActionData                               19 个持久化字段已有共用表单
├─ [√完全实现] actionType                            按类型切换相关字段
├─ [√完全实现] managedByScheduler                    是否由调度器管理
├─ [=部分实现] key                                   有敌人、预设别名、音乐及部分命令选择器
├─ [√完全实现] count                                 次数
├─ [√完全实现] preDelay                              动作前延时
├─ [√完全实现] interval                              重复间隔
├─ [=部分实现] routeIndex                            常规动作可选路线，部分演出引用仍依赖解析
├─ [√完全实现] blockFragment                         主波次片段等待
├─ [√完全实现] autoPreviewRoute                      自动预览路线
├─ [√完全实现] autoDisplayEnemyInfo                  自动展示敌人信息
├─ [√完全实现] isUnharmfulAndAlwaysCountAsKilled     特殊计数标记
├─ [√完全实现] hiddenGroup                           隐藏组
├─ [√完全实现] randomSpawnGroupKey                   随机组键
├─ [√完全实现] randomSpawnGroupPackKey               随机组包键
├─ [√完全实现] randomType                            随机类型
├─ [√完全实现] refreshType                           刷新类型
├─ [√完全实现] weight                                权重
├─ [√完全实现] dontBlockWave                         不阻塞波次
├─ [√完全实现] forceBlockWaveInBranch                分支强制阻塞波次
└─ useExtraRoute                                      运行时字段，历史 JSON 有该键时保留
```

## BlackboardPair、ESkillData、ESpData

```text
[√完全实现] BlackboardPair
├─ [√完全实现] key
├─ [√完全实现] value
└─ [√完全实现] valueStr

[√完全实现] ESkillData
├─ [√完全实现] prefabKey                             技能预制体键
├─ [√完全实现] priority                              优先级
├─ [√完全实现] cooldown                              冷却
├─ [√完全实现] initCooldown                          初始冷却
├─ [√完全实现] spCost                                技力消耗
└─ [√完全实现] blackboard[] → BlackboardPair

[√完全实现] ESpData
├─ [√完全实现] spType                                技力恢复方式
├─ [√完全实现] maxSp                                 最大技力
├─ [√完全实现] initSp                                初始技力
└─ [√完全实现] increment                             恢复增量
```

## PredefinedData

```text
[=部分实现] PredefinedData
├─ [=部分实现] characterInsts[]                      干员场上预设，共享字段见下
│  ├─ [√完全实现] position{row,col}                 地图位置
│  └─ [√完全实现] direction                         朝向
├─ [=部分实现] tokenInsts[]                          装置场上预设，共享字段见下
│  ├─ [√完全实现] position{row,col}
│  └─ [√完全实现] direction
├─ [=部分实现] characterCards[]                      干员卡牌，共享字段见下
└─ [=部分实现] tokenCards[]                          装置卡牌，共享字段见下
   └─ [√完全实现] initialCnt                        初始数量

四组记录的共享字段
├─ [√完全实现] hidden                               隐藏
├─ [√完全实现] alias                                预设别名
├─ [=部分实现] inst
│  ├─ [√完全实现] characterKey                      干员或装置 ID
│  ├─ [=部分实现] level / phase / potentialRank      干员有练度表单，装置按原生 JSON
│  ├─ [×还未实现or占位] favorBattlePhase             单条预设 JSON
│  └─ [×还未实现or占位] playerInstId                 单条预设 JSON
├─ [=部分实现] skillIndex / mainSkillLvl             干员技能及等级，含专一至专三
├─ [=部分实现] skinId / tmplId                        有目录数据的干员可选皮肤及适用形态
├─ [=部分实现] uniEquipIds[]{key,level}               单模组有表单，多模组用 JSON
├─ [×还未实现or占位] showSpIllust                     单条预设 JSON
├─ [×还未实现or占位] masterInfos[]{masterId,level}    单条预设 JSON
├─ [×还未实现or占位] overrideSkillBlackboard[]        单条预设 JSON
└─ [×还未实现or占位] overrideTalents[]                单条预设 JSON
   └─ prefabKey / blackboard[]
```
