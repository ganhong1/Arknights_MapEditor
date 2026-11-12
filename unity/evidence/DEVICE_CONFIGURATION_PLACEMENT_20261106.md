# 装置配置去重、地图放置与通用模板来源核验

日期：2026-11-06。按用户最新设计实施；通用模板选项只研究，不增加界面。没有启动游戏、实验服或修改 APW 原库。

## 当前数据契约

- 场上预设仅排除顶层 `position`、`direction` 后去重；`hidden`、`alias`、技能、黑板、未知字段、缺失／null／空数组继续区分。卡牌以完整记录去重。
- 界面直接选择一份配置，不出现配置组或组内记录选择器。地块及装置统一命名「全部既有已知配置」。
- 原始摆放信息只作为出处证据保存。当前配置出处合并全部匹配记录的正文路径；点击某路径可查看该正文内的每条完整原记录和数组索引。总出处覆盖当前使用形式的全部配置。
- schema v3：847 个目录项、403 个有样例 ID；6,919 份场上配置、811 份卡牌配置。14,003 份完整原记录及 20,694 条原数组出处全部回读一致。
- 默认推荐从相同配置集合选最多三份，第一份默认选中，不再选组。推荐不是游戏原生默认值，也不代表独立运行条件已满足。

## 放置与编辑

选择场上配置后，局部草稿不含原版坐标／朝向，启用「在地图放置装置」。使用现有 SceneView 的 Shift + 左键点击／拖动，目标格 `(row, col)` 和「放置朝向」生成这两个字段。方向名来自固定 PC 2.7.81 `SharedConsts.Direction`：UP=0、RIGHT=1、DOWN=2、LEFT=3；4 为哨兵，不允许作为放置选项。编辑器初始 RIGHT 不是原生默认值声明。

一次手势原子提交，重复经过同格只放一次；连续手势允许继续放置。不改变地形、卡牌或其他预设字段。编辑锁、越界、非法方向拒绝修改；带同一非空别名的多格放置或同层已有别名冲突拒绝，避免静默改名破坏引用。置空或选用地块退出装置放置。卡牌只通过插入预设使用，不人工补坐标。

旧配置缺坐标／朝向时，原始 JSON 插入／替换入口提示先放置或填写摆放值。完整原版出处仍可查看，但不会用原版坐标直接放进新地图。既有草稿覆盖取消机制保留。

## 通用模板研究结论

**装置有预制体，但装置预制体不等于 LevelData 预设模板。** 本轮只固定以下来源结论，不声称全部装置默认值或黑板键已穷举。

固定声明来源：APW 资源索引指向的 `source/ark-mix-source/DumpCS/2.7.81/dump-pc-x64-inspector-20261026.cs`。

| 数据层 | 已核声明／证据 | 能说明什么 |
|---|---|---|
| LevelData 预设 | 640482–640541：`PredefinedData.tokenInsts` 为 `PredefinedCharacter[]`，`tokenCards` 为 `PredefinedTokenCard[]` | 两种输入不同；前者增加 position/direction，后者增加 initialCnt |
| 共同输入基类 | 327711–327764 `CharacterInst`；149927–149940 `AdvancedCharacterInst`；640492–640508 `PredefinedInst` | inst、技能、皮肤、模板、覆盖黑板、覆盖天赋、模组等共同字段；hidden/alias 来自 PredefinedInst |
| 静态定义 | 322633 起 `CharacterData`；322858–322865 `PhaseData.characterPrefabKey` 等 | 装置静态定义、阶段属性、技能／天赋与预制体键并非整份预设记录 |
| 资源定位 | 826903–826907 `GetCharacterPath/GetTokenPath/GetSkillPath` | 字符／装置／技能有各自资源路径入口；声明不等于完整方法实现 |
| 实际预制体样本 | BattleLab 既有 `scratch/verification/objects-20261030/prefab-dependencies/remaining-prefab-components.json`，71 个样本 | 可读取 Trap、TrapMode、Ability 等组件序列化字段及资源引用；不是关卡 PredefinedInst 数据 |

预制体证据文件 SHA-256：`cf9d175c7d9cdb7f44614bfa93804cbebe6b49509fd13ba0c70eb72efd7ad0b6`。例如 `trap_003_gate` 的路径为 `dyn/battle/prefabs/[uc]tokens/trap_003_gate.prefab`，包 SHA-256 `7cb550eaf1afb27d9951b2b3bd9f7c5ec7d23a2ef18160ac274ce8fb8bc6de83`。根组件解析到 `Torappu.Battle.Trap`，另有 `TrapMode`、`PassiveBuffAbility`；字段包含模式、建造条件、方向指示和效果引用等。

71 份已有样本中未发现同时具有 inst/hidden/alias 的组件字段形状。这是有限样本核对，不能推导所有预制体不存在类似内容。它们来自先前资源批次，也没有重新宣称与当前 PC 声明逐构建绑定。

后续如提供通用配置，应区分：

1. 原生输入结构模板：按两个继承链列字段；构造器赋值与 null/空数组语义须另核，不能仅凭声明编造默认值。
2. 装置静态定义：角色阶段、技能和天赋数据；根据 level/phase/skill 等选择，不是统一恒定默认。
3. 预制体组件默认字段：显示已取得的真实序列化值及版本来源；不要转换成一份所谓完整 LevelData 默认记录。

因此暂不增加「查看通用配置 · 预制体默认字段」按钮。是否改为两个独立入口，应在来源和展示含义确认后决定。

## 验证

- Python 4 项：仅摆放差异合并、hidden/alias 保留区分、卡牌数量、null／missing／empty、推荐索引和原记录保留。
- Unity EditMode 41／41，通过任务 `70d1446daf494ff79fca1f4fd038883e`。覆盖全部目录读取、数据数量、出处、无原始记录选择器、插入卡牌、连续放置、地形不变、编辑锁、置空、原始字段保留、越界及重复别名原子拒绝，以及受影响既有 UI／草稿回归。
- 首轮测试发现连续放置第二次失败：首次应用后局部草稿被移除。修复为成功提交后只为同一配置建立新文档基线，其他编辑造成的过期状态仍拒绝。旧地块名称断言同步新命名后重跑通过。
- Unity 控制台 0 错误，已刷新当前工作台并展开装置资源库。当前地图 JSON 与完整草稿对比修改前一致；刷新前后的局部草稿一致。
- Ruff E4/E7/E9/F/I 与 `git diff --check` 通过。未做游戏效果验收。

本地证据位于工作区外的 `storage/map-editor-unity/device-library-placement-20261106/`：`roundtrip.json`、`prefab-research.json`、`unity-tests.json`、`workbench-verification.json`，生成数据位于 `data-v3/`。测试使用隔离文档，没有替换用户地图。
