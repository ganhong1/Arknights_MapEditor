# LevelData 可达类型与全量字段状态

固定基线为 PC 2.7.81。根字段的持久口径、历史样例与原生消费者见 [LevelData 全字段复核](LevelData全字段复核.md)。本页按类型定义列出嵌套字段，公共类型只展开一次。

## 容器与字段口径

- `mapData.map` 声明为 `short[,]`。历史 JSON 是行数组，每格指向 `tiles` 中的零起始配置索引。`width`、`height` 为 `[JsonIgnore]` 派生属性。格坐标 `GridPosition` 使用 `row/col`，向量采用 `Vector2{x,y}` 与 `Vector3{x,y,z}`。
- `ListDict<string,BranchData>` 在正文写成按分支键组织的对象。`optionalRunes` 为键到符文列表的对象，声明属性指定 `NullValueHandling.Ignore`。两处数据保持顺序和键原文。
- `Blackboard` 的列表元素为 `DataPair`。`TileData.blackboard` 的声明直接使用 `List<DataPair>`。每个 key 的值与消费者相关，结构本身可表达数字与字符串。`Undefinable<T>` 的 JSON 对象持有 `m_defined:bool`、`m_value:T`，两个字段都有 `[JsonProperty]`，私有存储也进入正文。
- 预设子类型会继承共享字段。`characterInsts/tokenInsts` 复用 `PredefinedCharacter`，`characterCards` 使用 `PredefinedCard`，`tokenCards` 通过 `PredefinedTokenCard` 加 `initialCnt`。`overrideSkillBlackboard`、`overrideTalents` 和模组／精通关联从继承链取得。
- ActionData 的 `useExtraRoute` 在固定 CLR 声明标为 `[NonSerialized]`，历史正文存在该键。编辑器保留原值并结合动作所在集合解析。`weightValue` 是 `[JsonIgnore]` 派生属性。历史正文中的缓存键与运行字段按原文兼容。
- 固定声明把 `AdvancedBuildableMask` 存在 TileData 运行字段中。图层枚举 `NONE=-1/LAYER_A=0/LAYER_B=1/E_NUM=2` 在地图消费者中定义。路线 `CheckpointTypeMask` 是另一套 `MOVE=1` 到 `PATROL_MOVE=256` 的选位值，和正文的 `CheckpointType` 序号分别处理。
- MapData 的 FlatBuffers vtable 字节偏移按声明次序为 `map=4`、`tiles=6`、`blockEdges=8`、`tags=10`、`effects=12`、`layerRects=14`。TileData 的持久项为 `tileKey=4`、`heightType=6`、`buildableType=8`、`passableMask=10`、`playerSideMask=12`、`blackboard=14`、`effects=16`。MapEffectData 的 `key/offset/direction` 为 `4/6/8`。
- TileData 构造器将 `buildableType` 和 `passableMask` 写为 3，`advancedBuildableMask` 写为 1。前两项的 FlatBuffers 解包缺省参数为 0，运行掩码没有对应槽。导入和新建应各自保留原状态，模板默认值在创建配置时显式应用。

表中空集合或空串涵盖 `[]`、`{}` 与 `""`，整数 `0` 计入标量。类型图包含运行时可达类型，正文的未知键仍保留。

## 根字段和正文状态

| 字段 | CLR 类型 | FlatBuffers vtable 偏移 | 缺失 | null | 空集合或空串 | 非空或标量 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `options` | `Options` | 4 | 0 | 0 | 0 | 8,664 |
| `levelId` | `string` | 6 | 0 | 8,637 | 0 | 27 |
| `mapId` | `string` | 8 | 0 | 8,664 | 0 | 0 |
| `bgmEvent` | `string` | 10 | 0 | 375 | 0 | 8,289 |
| `environmentSe` | `string` | 12 | 0 | 8,617 | 0 | 47 |
| `mapData` | `MapData` | 14 | 0 | 0 | 0 | 8,664 |
| `tilesDisallowToLocate` | `List<GridPosition>` | 16 | 0 | 267 | 8,397 | 0 |
| `runes` | `LegacyInLevelRuneData[]` | 18 | 0 | 2,240 | 1,771 | 4,653 |
| `optionalRunes` | `Dictionary<string, List<LegacyInLevelRuneData>>` | 20 | 698 | 4,447 | 3,418 | 101 |
| `globalBuffs` | `GlobalBuffData[]` | 22 | 0 | 3,438 | 3,196 | 2,030 |
| `routes` | `RouteData[]` | 24 | 0 | 1 | 97 | 8,566 |
| `extraRoutes` | `RouteData[]` | 26 | 77 | 0 | 5,976 | 2,611 |
| `enemies` | `EnemyData[]` | 28 | 0 | 0 | 8,647 | 17 |
| `enemyDbRefs` | `EnemyDataDbReference[]` | 30 | 0 | 0 | 69 | 8,595 |
| `waves` | `WaveData[]` | 32 | 0 | 0 | 38 | 8,626 |
| `branches` | `ListDict<string, BranchData>` | 34 | 0 | 3,397 | 2,595 | 2,672 |
| `predefines` | `PredefinedData` | 36 | 0 | 400 | 0 | 8,264 |
| `hardPredefines` | `PredefinedData` | 38 | 29 | 925 | 0 | 7,710 |
| `excludeCharIdList` | `string[]` | 40 | 0 | 4,967 | 3,697 | 0 |
| `randomSeed` | `int` | 42 | 0 | 0 | 0 | 8,664 |
| `operaConfig` | `string` | 44 | 69 | 8,376 | 0 | 219 |
| `cameraPlugin` | `string` | 46 | 490 | 5,063 | 3,105 | 6 |
| `runtimeData` | `RuntimeData` | — | 4,756 | 3,908 | 0 | 0 |

偏移为 `Unpack_Torappu_LevelData` 的 vtable 字节偏移。对应零起始字段序号 `(偏移−4)/2`。字段顺序依 LevelData 声明。

## 可达类型、字段及枚举

### `LevelData.Options`

```text
characterLimit  int
maxLifePoint  int
initialCost  int
maxCost  int
costIncreaseTime  float
moveMultiplier  float
steeringEnabled  bool
reachableCheckIgnoreStartTile  bool
isTrainingLevel  bool
isHardTrainingLevel  bool
isPredefinedCardsSelectable  bool
displayRestTime  bool
maxPlayTime  float
functionDisableMask  BattleFunctionDisableMask
configBlackBoard  Torappu.Blackboard
enemyTauntLevelPow  int  [NonSerialized]
deployCostPostDelta  int  [NonSerialized]
deployCostPostDeltaMinCost  int  [NonSerialized]
```

### `BattleFunctionDisableMask`

`NONE=0` · `CARD_LIST=1` · `CHARACTER_MENU=2` · `CHARACTER_INFO=4` · `SYSTEM_MENU=8` · `PAUSE_BUTTON=16` · `SPEED_SWITCHER_BUTTON=32` · `BATTLE_STATUS=64` · `COST_PANEL=128` · `SLOW_MOTION=256` · `PAUSE_BUTTON_INTERACT=512` · `SYSTEM_MENU_INTERACT=1024` · `SPEED_SWITCHER_BUTTON_INTERACT=2048` · `UNIT_HUD_SKILL_CAST_MASK=4096` · `WITHDRAWABLE_PANEL=8192` · `COST_PANEL_KEEP_CHARACTERLIMIT=16384` · `CHARACTER_LIMIT=32768` · `AUTOCHESS_SELL_OR_DESTORY=65536` · `CHARACTER_MENU_PANEL=131072` · `DRAG_UI_CARD=262144` · `ALL=524287`

### `Blackboard`

声明基类、底层类型或接口 `List<Torappu.Blackboard.DataPair>, IHotfixable`。

本类型无新增实例字段。

### `MapData`

```text
map  short[,]
tiles  TileData[]
blockEdges  Edge[]
tags  string[]
effects  MapEffectData[]
layerRects  string[]
```

### `TileData`

```text
tileKey  string
heightType  HeightType
buildableType  BuildableType
passableMask  MotionMask
playerSideMask  PlayerSideMask
advancedBuildableMask  AdvancedBuildableMask  [NonSerialized]
blackboard  List<Torappu.Blackboard.DataPair>
effects  MapEffectData[]
```

### `TileData.HeightType`

`LOWLAND=0` · `HIGHLAND=1` · `E_NUM=2`

### `BuildableType`

`NONE=0` · `MELEE=1` · `RANGED=2` · `ALL=3`

### `MotionMask`

`NONE=0` · `WALK_ONLY=1` · `FLY_ONLY=2` · `ALL=3`

### `PlayerSideMask`

声明基类、底层类型或接口 `byte`。

`ALL=0` · `SIDE_A=2` · `SIDE_B=4` · `NONE=255`

### `Battle.AdvancedBuildableMask`

`NONE=0` · `DEFAULT=1` · `DEEP_SEA=2` · `TIDE_SEA=4` · `NIGHT=8` · `HIDE=16` · `WOODRD_HOLE=32` · `RIDGE_FIELD=64` · `ENEMY_FTPRG=128` · `RED_FOG=256` · `ACT47SIDE_DURING_BALLOON_FLOAT=512` · `ACT47SIDE_BANNED=1024`

### `Blackboard.DataPair`

```text
key  string
value  float
valueStr  string
```

### `MapEffectData`

```text
key  string
offset  UnityEngine.Vector3
direction  SharedConsts.Direction
```

### `SharedConsts.Direction`

`UP=0` · `RIGHT=1` · `DOWN=2` · `LEFT=3` · `E_NUM=4` · `INVALID=4`

### `MapData.Edge`

```text
pos  GridPosition
direction  SharedConsts.Direction
blockMask  MotionMask
```

### `GridPosition`

声明基类、底层类型或接口 `IComparable<GridPosition>, IEquatable<GridPosition>`。

```text
row  int
col  int
```

### `LegacyInLevelRuneData`

```text
difficultyMask  Torappu.LevelData.Difficulty
key  string
professionMask  ProfessionCategory
buildableMask  BuildableType
blackboard  Torappu.Blackboard
```

### `LevelData.Difficulty`

`NONE=0` · `NORMAL=1` · `FOUR_STAR=2` · `EASY=4` · `SIX_STAR=8` · `ALL=15`

### `ProfessionCategory`

`NONE=0` · `WARRIOR=1` · `SNIPER=2` · `TANK=4` · `MEDIC=8` · `SUPPORT=16` · `CASTER=32` · `SPECIAL=64` · `TOKEN=128` · `TRAP=256` · `PIONEER=512`

### `LevelData.GlobalBuffData`

```text
prefabKey  string
blackboard  Torappu.Blackboard
overrideCameraEffect  string
passProfessionMaskFlag  bool
professionMask  ProfessionCategory
playerSideMask  PlayerSideMask
useExtraData  bool  [NonSerialized]
extraRuntimeData  ExtraRuntimeData  [NonSerialized]
source  IGlobalBuffSource  [NonSerialized]
```

### `LevelData.GlobalBuffData.ExtraRuntimeData`

此处由运行数据可达。

```text
sideType  SideType
extraValidator  GlobalBuff.GlobalBuffExtraValidatorDelegate
checkExtraProfession  bool
```

### `Battle.SideType`

`NONE=0` · `ALLY=1` · `ENEMY=2` · `BOTH_ALLY_AND_ENEMY=3` · `NEUTRAL=4` · `ALL=7`

### `RouteData`

```text
motionMode  MotionMode
startPosition  GridPosition
endPosition  GridPosition
spawnRandomRange  Vector2
spawnOffset  Vector2
checkpoints  CheckpointData[]
allowDiagonalMove  bool
visitEveryTileCenter  bool
visitEveryNodeCenter  bool
visitEveryCheckPoint  bool
```

### `MotionMode`

`WALK=0` · `FLY=1` · `E_NUM=2`

### `RouteData.CheckpointData`

```text
type  CheckpointType
time  float
position  GridPosition
reachOffset  Vector2
randomizeReachOffset  bool
reachDistance  float
```

### `CheckpointType`

`MOVE=0` · `WAIT_FOR_SECONDS=1` · `WAIT_FOR_PLAY_TIME=2` · `WAIT_CURRENT_FRAGMENT_TIME=3` · `WAIT_CURRENT_WAVE_TIME=4` · `DISAPPEAR=5` · `APPEAR_AT_POS=6` · `ALERT=7` · `PATROL_MOVE=8` · `WAIT_BOSSRUSH_WAVE=9` · `MAP_OFFSET_MOVE=10` · `INVALID=11`

### `LevelData.EnemyData`

```text
name  string
description  string
key  string
attributes  Torappu.AttributesData
applyWay  SourceApplyWay
motion  MotionMode
enemyTags  string[]
notCountInTotal  bool
alias  string
lifePointReduce  int
rangeRadius  float
numOfExtraDrops  int
viewRadius  float
levelType  EnemyLevelType
talentBlackboard  Torappu.Blackboard
skills  ESkillData[]
spData  ESpData
m_runtimeData  RuntimeData  [private runtime backing field]
```

### `AttributesData`

```text
maxHp  ObscuredInt
atk  ObscuredInt
def  ObscuredInt
magicResistance  ObscuredFloat
cost  ObscuredInt
blockCnt  ObscuredInt
moveSpeed  ObscuredFloat
attackSpeed  ObscuredFloat
baseAttackTime  ObscuredFloat
respawnTime  ObscuredInt
hpRecoveryPerSec  ObscuredFloat
spRecoveryPerSec  ObscuredFloat
maxDeployCount  ObscuredInt
maxDeckStackCnt  ObscuredInt
tauntLevel  ObscuredInt
massLevel  ObscuredInt
baseForceLevel  ObscuredInt
epDamageResistance  ObscuredFloat
epResistance  ObscuredFloat
damageHitratePhysical  ObscuredFloat
damageHitrateMagical  ObscuredFloat
abilityRangeForwardExtend  ObscuredFloat
defPenetrate  ObscuredFloat
magicResistPenetrate  ObscuredFloat
hpRecoveryPerSecByMaxHpRatio  ObscuredFloat
defPenetrateFixed  ObscuredFloat
oneMinusStatusResistance  ObscuredFloat
magicResistPenetrateFixed  ObscuredFloat
maxEp  ObscuredInt
epRecoveryPerSec  ObscuredFloat
spRecoverRatio  ObscuredFloat
epBreakRecoverSpeed  ObscuredFloat
slowDown  ObscuredFloat
blockRadiusScale  ObscuredFloat
stunImmune  bool
silenceImmune  bool
sleepImmune  bool
frozenImmune  bool
levitateImmune  bool
disarmedCombatImmune  bool
fearedImmune  bool
palsyImmune  bool
attractImmune  bool
teleportImmune  bool
groundBoundImmune  bool
```

### `CodeStage.AntiCheat.ObscuredTypes.ObscuredInt`

声明基类、底层类型或接口 `IFormattable, IEquatable<ObscuredInt>, IComparable<ObscuredInt>, IComparable<int>, IComparable`。

运行时标量封装内部字段。原版内联敌人 JSON 使用数值。

```text
currentCryptoKey  int
hiddenValue  int
inited  bool
fakeValue  int
fakeValueActive  bool
```

### `CodeStage.AntiCheat.ObscuredTypes.ObscuredFloat`

声明基类、底层类型或接口 `IFormattable, IEquatable<ObscuredFloat>, IComparable<ObscuredFloat>, IComparable<float>, IComparable`。

运行时标量封装内部字段。原版内联敌人 JSON 使用数值。

```text
currentCryptoKey  int
hiddenValue  int
hiddenValueOldByte4  ACTkByte4
inited  bool
fakeValue  float
fakeValueActive  bool
```

### `CodeStage.AntiCheat.Common.ACTkByte4`

运行时标量封装内部字段。原版内联敌人 JSON 使用数值。

```text
b1  byte
b2  byte
b3  byte
b4  byte
```

### `SourceApplyWay`

`NONE=0` · `MELEE=1` · `RANGED=2` · `ALL=3`

### `EnemyLevelType`

`NORMAL=0` · `ELITE=1` · `BOSS=2` · `E_NUM=3`

### `LevelData.EnemyData.ESkillData`

```text
prefabKey  string
priority  int
cooldown  float
initCooldown  float
spCost  int
blackboard  Torappu.Blackboard
```

### `LevelData.EnemyData.ESpData`

```text
spType  SpType
maxSp  int
initSp  int
increment  float
```

### `SpType`

`NONE=0` · `INCREASE_WITH_TIME=1` · `INCREASE_WHEN_ATTACK=2` · `INCREASE_WHEN_TAKEN_DAMAGE=4` · `ATTACK_OR_DAMAGE=6` · `ALL=7`

### `LevelData.EnemyData.RuntimeData`

此处由运行数据可达。

```text
dynamicAbilities  List<DynamicAbilityData>
skinData  List<string>
```

### `DynamicAbilityData`

此处由运行数据可达。

```text
atRoot  bool
prefabKey  string
blackboard  Torappu.Blackboard
```

### `LevelData.EnemyDataDbReference`

```text
useDb  bool
id  string
level  int
overwrittenData  EnemyDatabase.EnemyData
```

### `EnemyDatabase.EnemyData`

```text
name  Undefinable<string>
description  Undefinable<string>
prefabKey  Undefinable<string>
attributes  Torappu.EnemyDatabase.AttributesData
applyWay  Undefinable<SourceApplyWay>
motion  Undefinable<MotionMode>
enemyTags  Undefinable<string[]>
lifePointReduce  Undefinable<int>
levelType  Undefinable<EnemyLevelType>
rangeRadius  Undefinable<float>
numOfExtraDrops  Undefinable<int>
viewRadius  Undefinable<float>
notCountInTotal  Undefinable<bool>
talentBlackboard  Torappu.Blackboard
skills  Torappu.LevelData.EnemyData.ESkillData[]
spData  Torappu.LevelData.EnemyData.ESpData
```

### `EnemyDatabase.AttributesData`

```text
maxHp  Undefinable<int>
atk  Undefinable<int>
def  Undefinable<int>
magicResistance  Undefinable<float>
cost  Undefinable<int>
blockCnt  Undefinable<int>
moveSpeed  Undefinable<float>
attackSpeed  Undefinable<float>
baseAttackTime  Undefinable<float>
respawnTime  Undefinable<int>
hpRecoveryPerSec  Undefinable<float>
spRecoveryPerSec  Undefinable<float>
maxDeployCount  Undefinable<int>
massLevel  Undefinable<int>
baseForceLevel  Undefinable<int>
tauntLevel  Undefinable<int>
epDamageResistance  Undefinable<float>
epResistance  Undefinable<float>
damageHitratePhysical  Undefinable<float>
damageHitrateMagical  Undefinable<float>
epBreakRecoverSpeed  Undefinable<float>
stunImmune  Undefinable<bool>
silenceImmune  Undefinable<bool>
sleepImmune  Undefinable<bool>
frozenImmune  Undefinable<bool>
levitateImmune  Undefinable<bool>
disarmedCombatImmune  Undefinable<bool>
fearedImmune  Undefinable<bool>
palsyImmune  Undefinable<bool>
attractImmune  Undefinable<bool>
teleportImmune  Undefinable<bool>
groundBoundImmune  Undefinable<bool>
```

### `LevelData.WaveData`

```text
preDelay  float
postDelay  float
maxTimeWaitingForNextWave  float
fragments  FragmentData[]
advancedWaveTag  string
```

### `LevelData.WaveData.FragmentData`

```text
preDelay  float
actions  ActionData[]
```

### `LevelData.WaveData.FragmentData.ActionData`

声明基类、底层类型或接口 `IItemWithWeight, IReusable`。

```text
actionType  ActionType
managedByScheduler  bool
key  string
count  int
preDelay  float
interval  float
useExtraRoute  bool  [NonSerialized]
routeIndex  int
blockFragment  bool
autoPreviewRoute  bool
autoDisplayEnemyInfo  bool
isUnharmfulAndAlwaysCountAsKilled  bool
hiddenGroup  string
randomSpawnGroupKey  string
randomSpawnGroupPackKey  string
randomType  RandomType
refreshType  RefreshType
weight  int
dontBlockWave  bool
forceBlockWaveInBranch  bool
isValid  bool  [NonSerialized]
notCountInTotal  bool  [NonSerialized]
extraMeta  object  [NonSerialized]
actionId  ActionID  [NonSerialized]
```

### `LevelData.WaveData.FragmentData.ActionData.ActionType`

`SPAWN=0` · `PREVIEW_CURSOR=1` · `STORY=2` · `TUTORIAL=3` · `PLAY_BGM=4` · `DISPLAY_ENEMY_INFO=5` · `ACTIVATE_PREDEFINED=6` · `PLAY_OPERA=7` · `TRIGGER_PREDEFINED=8` · `BATTLE_EVENTS=9` · `WITHDRAW_PREDEFINED=10` · `DIALOG=11` · `SHOW_ALL_HIDDEN_CARDS=12` · `EMPTY=13` · `E_NUM=14`

### `LevelData.WaveData.FragmentData.ActionData.RandomType`

`ALWAYS=0` · `PER_DAY=1` · `NEVER=2` · `PER_SETTLE_DAY=3` · `PER_SEASON=4`

### `LevelData.WaveData.FragmentData.ActionData.RefreshType`

`ALWAYS=0` · `PER_DAY=1` · `NEVER=2` · `PER_SETTLE_DAY=3` · `PER_SEASON=4`

### `LevelData.ActionID`

此处由运行数据可达。

```text
subSchedulerI  int
waveI  int
fragI  int
actionI  int
```

### `LevelData.BranchData`

```text
phases  PhaseData[]
```

### `LevelData.BranchData.PhaseData`

```text
preDelay  float
actions  WaveData.FragmentData.ActionData[]
m_randomActionGroups  Dictionary<string,List<ActionData>>  [NonSerialized]
m_actionWithRandomSpawn  List<ActionData>  [NonSerialized]
m_validActionPackKeys  HashSet<string>  [NonSerialized]
```

### `LevelData.PredefinedData`

```text
characterInsts  PredefinedCharacter[]
tokenInsts  PredefinedCharacter[]
characterCards  PredefinedCard[]
tokenCards  PredefinedTokenCard[]
```

### `LevelData.PredefinedData.PredefinedCharacter`

声明基类、底层类型或接口 `PredefinedInst`。

```text
position  GridPosition
direction  SharedConsts.Direction
```

### `LevelData.PredefinedData.PredefinedInst`

声明基类、底层类型或接口 `AdvancedCharacterInst`。

```text
hidden  bool
alias  string
```

### `AdvancedCharacterInst`

声明基类、底层类型或接口 `CharacterInst`。

```text
uniEquipIds  CharacterData.UniqueEquipPair[]
showSpIllust  bool
masterInfos  CharacterData.MasterInfo[]
```

### `CharacterInst`

```text
inst  Metadata
skillIndex  int
mainSkillLvl  int
skinId  string
tmplId  string
overrideSkillBlackboard  Torappu.Blackboard
overrideTalents  TalentInst[]
```

### `CharacterInst.Metadata`

```text
characterKey  string
level  int
phase  EvolvePhase
favorBattlePhase  int
potentialRank  int
playerInstId  int
```

### `EvolvePhase`

`PHASE_0=0` · `PHASE_1=1` · `PHASE_2=2` · `PHASE_3=3` · `E_NUM=4`

### `CharacterInst.TalentInst`

```text
prefabKey  string
blackboard  Torappu.Blackboard
```

### `CharacterData.UniqueEquipPair`

```text
key  string
level  int
```

### `CharacterData.MasterInfo`

```text
masterId  string
level  int
```

### `LevelData.PredefinedData.PredefinedCard`

声明基类、底层类型或接口 `PredefinedInst`。

本类型无新增实例字段。

### `LevelData.PredefinedData.PredefinedTokenCard`

声明基类、底层类型或接口 `PredefinedCard`。

```text
initialCnt  int
```

### `LevelData.RuntimeData`

此处由运行数据可达。

```text
extraLoadEnemies  List<EnemyDataDbReference>
```
