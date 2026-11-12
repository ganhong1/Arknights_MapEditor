# LevelData 模式影响与原版全量数据核验

日期为 2026-11-05。本次采用源码消费者和全量原版配置交叉核验。编辑器模式配置界面尚待实施。研究来源与原始数据、客户端运行证据分别记录。

## 输入与范围

- 原版数据取 APW 资源根下 `source/ArknightsGameData/zh_CN/gamedata`，Git 提交为 `bb8f9ac8db143a661577ed6ef5184d3c6e93d1d0`。扫描时工作数据干净，全部 3,901 个 JSON 文件均受该仓库跟踪。
- 类型声明取 `source/ark-mix-source/DumpCS/2.7.81/dump-pc-x64-source.cs`。固定 PC 原生字节取资源导航登记的 `pc-disk-buff-byte-binding-1322/GameAssembly.dll`，原始 DLL 哈希写入外部证据。原版数据快照与客户端字节各有独立身份。
- 审计工具为 [audit_level_modes.py](../../tools/audit_level_modes.py)，针对性测试为 [test_audit_level_modes.py](../../tools/tests/test_audit_level_modes.py)。工具只读取数据，以新文件发布证据，拒绝覆盖旧报告或将报告写回数据源目录。
- 外部证据根采用本机研究目录配置，逻辑位置为 `storage/map-editor-unity/level-mode-audit-20261105`。其中 `corpus-05.json` 是当前含原生资源映射的全量结果，`pair-details.json` 保存不同正文的逐字段与格子差异，`native-consumers.json` 与 `native-selectors.json` 保存原生窗口地址、大小、哈希与解释边界。
- 旧 `corpus-01` 至 `corpus-04` 保留调查过程，不能覆盖 `corpus-05` 的资源解析结论。

## 全量配置结果

| 核验内容 | 结果 |
| --- | --- |
| 扫描 JSON 文件 | 3,901 |
| 可识别 LevelData 正文 | 3,899 |
| 其他 JSON | 2，分别为敌人数据库与 `levels_meta.json` |
| JSON 解析错误 | 0，重复键和非有限 JSON 常量采用拒绝策略 |
| 有矩形布局的正文 | 3,898 |
| 核验的格子／地块索引 | 479,961，未发现越界或布尔索引 |
| 无布局正文 | 1，`obt/sandbox/sandbox_2/level_sandbox2_empty.json` 的 map 和 tiles 均为 null |
| 正文字段组合 | 8 种，保留缺失、null、空集合差异 |
| 带旧导出 runtimeData 字段 | 215，全部为 null；当前 PC 类型声明将其标为 NonSerialized |
| 非空 optionalRunes 正文 | 46，45 份主线、1 份肉鸽正文 |
| 带 hiddenGroup 动作的正文 | 839 |
| 基础及可选符文掩码记录 | 6,978 |

无布局正文只是输入事实。它在具体玩法中的地图补全流程尚待追踪，不能标记为已验证可直接启动的地图，也不能据此推断资源损坏。

## 关卡身份到正文的原生映射

直接把 `StageData.levelId` 拼成资源文件名，会错误地产生 108 条未解析关系。PC `ResourceCollector.GetLevelDataPath` 会调用 `BattleMiscDB.TryGetSceneId`，从 `battle/battle_misc_table.json` 的 `levelScenePairs` 进行一次精确键查找，然后使用该条目的 `levelId` 取得正文。该表包含 586 条记录。

例如 `Obt/Main/level_easy_09-01` 映射到 `Obt/Main/level_main_09-01`。同一个条目还有 `sceneId` 和 `hookedMapPreviewId`，分别承担其他关联职责。正文映射不递归追踪，也不以 sceneId 替代正文路径。

| 显式关系范围 | 已解析 | 共用正文 | 不同正文 |
| --- | --- | --- | --- |
| 全部 stage 类型 | 1,008 | 923 | 85 |
| MAIN 主线 | 352 | 268 | 84 |

主线的 268 条共用关系由 121 条 `hardStagedId`、45 条 `sixStarStageId` 和 102 条环境难度关系组成。另有 84 条环境难度关系使用不同正文。关系只来自原字段和 diffGroupTable，不按名称前缀猜测族关系。

剩余 5 条未解析关系均为 `act4d0_01` 至 `act4d0_05` 的普通／突袭关系。当前数据源缺对应正文，映射表也未补齐。该结论限定本地快照，不扩展为云端或历史资源不存在。

## 主线原始正文差异

352 条主线显式关系均已按上述映射解析。下表统计其中 84 条不同正文关系的顶层原始值差异。

| 字段 | 有差异的主线关系数 |
| --- | --- |
| runes | 84 |
| enemyDbRefs | 47 |
| waves | 47 |
| routes | 29 |
| predefines | 26 |
| mapData | 18 |
| randomSeed | 13 |
| extraRoutes | 8 |
| branches | 4 |
| options | 3 |
| hardPredefines | 2 |

这对应 11／22 个当前序列化顶层字段，即 50% 出现过原始数据差异。此比例只描述这批主线显式关系中的观察结果，不能解释为全玩法的可修改字段比例或模式权限白名单。此前聊天中 27.3% 和 54.5% 混合了输入选择、正文差异和运行效果，不再作为设计指标。

其中两处 hardPredefines 差异都只是 null 与四个空集合之间的变化，两侧 isHardTrainingLevel 均为 false。本体合成路径不会据此产生不同的困难角色队伍。保留这两处原始差异，另外标记存储形式和本体条件，不能直接签为行为差异。

进一步解析 mapData 的格子索引后，18 对主线正文都存在实际逐格配置差异，排除了仅由调色板重排产生的假差异。其中两对还存在宽度变化，以下以行×列表示。

- `main_10-06` 对 `tough_10-06` 为 8×11 对 8×13。
- `main_12-18` 对 `tough_12-18` 为 8×12 对 8×13。

options 的具体差异为 `main_10-15` 的 characterLimit 8 对 9、`main_12-03` 的 characterLimit 8 对 6，以及 `main_12-16` 的 initialCost 10 对 5。

## 共用正文的条件处理

PC `LegacyInLevelRuneData.CheckValidForDifficulty` 的 16 字节窗口闭合了实际谓词 `(mask & requested) == requested`。它要求包含全部请求位。requested 为 NONE 时，该单独谓词返回 true；不能将此结果扩展为 NONE 是有效开战难度。

工具按该谓词记录候选基础符文列表的摘要。121 条主线普通／突袭和 45 条普通／六星关系都共用正文，但按各自难度筛选后的基础符文记录不同。该比较尚未执行选择器、叠层计算、原生效果或账号资格检查。

45 个主线 SIX_STAR 条目的 180 个进阶条件 ID 均能经 `sixStarRuneData.runeKey` 精确关联到解析后正文中的 optionalRunes 包。没有根据 canPractice 推断沙盘资格，也没有替用户选择具体条件。

46 份非空 optionalRunes 中有一份属于肉鸽。编辑器中的该容器应作为通用可选符文包保存，再由模式适配器提供选择语义。

## 原生消费者证据与开放边界

本轮冻结 20 个方法字节窗口，共 16,880 字节。核验内容包括困难预设合成、关卡规则处理、地块黑板处理、额外禁用位置、预设技能替换、装置位置处理、肉鸽预设处理、自走棋波次重建、难度判定及正文映射。新增 `native-ownership.json` 记录地块数据复制和运行持有关系。

LoadGameWithRune 的整个声明区间包含未被线性反汇编完整解码的尾部，已记录原始窗口哈希及解码长度。当前只能引用已解码的处理调用段，不能宣称该函数完整控制流已闭合。其他专用玩法重写、动态分派和热更新仍待分别追踪。本轮没有新的游戏运行验证。

地块复制路径进一步闭合。RuneManager.PreprocessTile 在存在 MAP 目标处理的分支调用 TileData.DeepClone，再把克隆交给处理器；Tile.Init 将返回值存入运行地块的数据成员。未匹配该处理条件时存在直接返回输入的分支，因此不能统一声明所有运行地块始终独占原始 TileData。Options 对应路径调用 `0x1845633d0` 泛型转换后再处理，其具体复制和共享契约仍需绑定类型，不能只根据调用形状签为完整深复制。

预设还存在独立的选择约束。CheckStagePredefine 对 FOUR_STAR 检查 isHardTrainingLevel，对其他请求难度检查 isTrainingLevel。GenPredefinedCardsToInject 先检查 isPredefinedCardsSelectable，再决定是否提供预设卡牌；FOUR_STAR 路径采用组合预设。它们与 LoadComposeHardPredefine 内部条件共同决定结果，单独读取一个布尔字段不足以代表完整编队行为。

## 编辑器设计约束

1. 分别保存逻辑关卡 ID、解析后的正文资源 ID 和场景资源关联。资源缺失诊断必须先经过原生映射。
2. 支持同一正文上的条件选择，以及同一关卡族的多正文变体。变体可能拥有不同地图尺寸、路线和装置。
3. 原始正文、候选输入、加载副本、额外输出和实际运行实例分别记录。原始数据差异与行为差异使用不同状态。
4. 保留缺失、null、空集合、数组顺序及历史附加字段。存储形式差异不自动升级为行为差异。
5. 所有未核验模式路径保持开放问题，不将当前样本中未发生变化的字段设置成原生禁止修改。

## 验证与下一队列

本轮 31 项定向测试通过，审计脚本行覆盖率 92%，Ruff E4/E7/E9/F/I 检查通过。测试覆盖掩码非法值、全请求位判断、重复 JSON 键、缺失与 null 区分、调色板重排、越界索引、无布局正文、显式关卡关系、单次原生正文映射，以及输出不能覆盖旧证据或污染源数据。

下一队列为调用方到消费者的写入对象与复制关系闭合、分支动作筛选与原生映射版本绑定、无布局正文的专用模式补全流程，以及 5 条旧活动引用的补源。集中运行差分需另有明确任务授权后执行。

当前权威外部报告 corpus-05.json 的 SHA-256 为 `93c26e0cec4d2e95cd2f2950cb5cfdaa73fe7cf1af490faaefcd26c240437808`。
