# Unity LevelData 编辑器复盘与本地提交准备

复核日期 2026-11-12。审查对象为地图编辑器仓库的两个本地工作树。原生接口基线为 PC 2.7.81，Unity 运行及测试基线为 2021.3.39f1。

## 分支与工作树

| 本地分支 | 提交 | 关联工作树 | 本地状态 |
| --- | --- | --- | --- |
| `main` | `ec480b6` | D 盘正式仓库 | 跟踪本地缓存的 `origin/main`。未跟踪内容为 28 个设计或历史证据文件。 |
| `feature/unity-leveldata-editor` | `ec480b6` | Codex 托管工作树 | 当前开发分支。目录沿用 `map-editor-tile-object-matching` 命名，分支以 Git 为准。 |
| `feature/tile-object-matching` | `ec480b6` | 未挂载 | 现有功能分支，暂无独有提交。本轮 Web 批次并入 Unity 功能分支。 |
| `dev` | `ec480b6` | 未挂载 | 本地开发分支，暂无独有提交。 |
| `hotfix` | `ec480b6` | 未挂载 | 本地修复分支，暂无独有提交。 |

五个分支与本地缓存的 `origin/main` 均指向 `ec480b64130da4a3dbe2a8ca4c6ea72e0f0613b0`。当前功能工作树有 8 个已跟踪文件发生变化，均属 Web 与桌面壳，Unity 包、主要设计与工具仍是未跟踪内容。两个工作树的索引均无暂存。此次只读检查 Git 分支、提交与工作树，未刷新远端引用。

按相对路径和 SHA-256 对照复盘前的两份工作树，24 份同路径文件完全相同，3 份同路径文件在功能工作树中有较新内容。功能工作树的设计导航、Unity 主设计和实施计划作为当前版本。D 盘 `main` 独有的 `ProjectDesignSchemeDocument/LEVELDATA_STRUCTURE.md` 保留原件，作为原生结构历史资料，本次新矩阵另存。

## 设计与代码现状

| 设计方向 | 当前代码与验收范围 |
| --- | --- |
| 工程边界 | Editor-only UPM `0.1.0-preview.1`，Unity 2021.3.39f1、C#、UI Toolkit/USS、Newtonsoft.Json 3.2.2。Core 负责原生 JSON、索引和引用，Storage 负责保存与恢复，Application 和 UI 管理会话。Web HTML/CSS/JS 与 Rust 桌面壳继续独立。 |
| Web 与桌面 | 左右抽屉、资源高亮、PRTSMap 2D／3D、Canvas 回退、自由视角和滚轮缩放。地形详情映射到 Wiki 页面，保留外部打开入口。桌面资源路由和 EXE 打包清单已随前端更新。 |
| 文档与工作台 | 新建、打开、保存、另存、撤销、重做、外部文件冲突、草稿恢复。中间字段树按父子结构读取应用后的正文，左编辑、右资源抽屉可独立收起与拖拽。全量 JSON 和局部草稿需显式应用。 |
| 原生场景与骨架 | 多地图标签、双原生 SceneView、独立相机与编辑／相机锁。骨架由 Unity 渲染，包含地块外观、实体标记、预设手牌和路线。全地块与物件总览作为展示样本；资源库条数与展示样本条数采用各自的口径。 |
| 地形与 TileData | 116 个原生地块键、1 个虚拟箱子示意项；地图矩阵、尺寸及裁剪、共享配置和多格绑定已接。资源详情支持变种、最多三项推荐、全部既有已知配置、配置出处及关卡预览。 |
| 关卡预设 | 基础／困难预设各自保留场上干员、干员卡、场上装置、装置卡。装置目录 847 项，干员目录 461 项。右侧搜索、标签筛选、详情与选用，左侧编辑参数和编队复制，SceneView 可展示手牌。 |
| Options | 新建写入 15 字段默认对象，逐字段表单、样例推荐、全部已知值、按关卡分组的出处、内嵌黑板和掩码已接。音频两个顶层字段独立使用片段编辑。 |
| 路线与调度 | 主／额外路线编辑和 PRTSMap 式单条、按时、按序演示。波次和分支动作具备部分编辑。敌人定义、独立分支和符文区仍以字段定位与高级 JSON 为主。 |
| 成品关卡地图 | 3,461 个关卡组、4,741 个关卡 ID、8,664 份语义去重正文，保留模式和来源版本。正文约 1.12 GB，包内 ZIP 为 40,230,466 字节。既有全量记录为 8,662 份骨架出图，2 份空模板，地块与可见实体回退均为零。 |
| 美术 | `.arkmap` 可保存独立美术映射。5-10 的场景、光照、地图物件与红蓝门在隔离工程完成组装预览。通用 `mapId` 场景映射、资源选择和按版本解析待接入工作台。 |
| 搜索和来源 | Unity 目录复用 AngelinaTravelToolbox 方案所用 Fuse.js、拼音与 Bitap 兼容链。资源来源分开记录。成品正文包含多个历史快照，原生研究仍使用 2.7.81。 |

场景加载、资源包运行、关卡启动与战斗效果归各自的运行环境验收。完整编辑覆盖见 [LevelData 字段矩阵](LEVELDATA_EDITOR_FIELD_MATRIX.md)。主要设计入口为 [Unity 编辑器设计](UNITY_EDITOR_DESIGN.md)、[主实施计划](PlanDocumentsForImplementation/UnityEditor/UNITY_EDITOR_IMPLEMENTATION_PLAN.md)、[地图美术研究](ART_RESOURCE_CODE_AND_ASSET_RESEARCH.md) 和 [5-10 组装记录](BATTLE510_ART_ASSEMBLY.md)。

## 拟定提交

按路径分成五批。经审核，A–E 依次进入 `feature/unity-leveldata-editor`。A 单独保留 Web 与桌面壳提交边界，B–E 依次提交 Unity 插件、测试、目录生成工具和文档。

| 批次 | 目标分支及建议提交信息 | 文件边界 |
| --- | --- | --- |
| A | `feature/unity-leveldata-editor` · `feat(web): add PRTSMap views and terrain drawers` | 8 个已跟踪修改、`drawers.js`、`tile-info.js`、`tile-palette.js`、`assets/`、`prts-map/`。包含 Web 数据模型测试的 fixture 隔离修复。 |
| B | `feature/unity-leveldata-editor` · `feat(unity): add native LevelData editor` | UPM 包本体、工作台代码、目录、字体、图集、ZIP、着色器、样例、包说明及 `.meta`，排除包内测试。资源与代码同批保证工作台首次打开和既有样例可用。 |
| C | `feature/unity-leveldata-editor` · `test(unity): cover LevelData editor workflows` | 包内 `Tests/` 与 `Tests.meta`、开发工程与测试脚本、`unity/verification/` 的验收和独立美术探针。包含本次回归断言及测试条件修复。 |
| D | `feature/unity-leveldata-editor` · `build(catalog): add pinned resource generators` | 新增 `tools/` 调查、索引和验证脚本，`unity/verification/build-*` 生成器及 Python 测试。生成输入使用明确的本地来源参数。 |
| E | `feature/unity-leveldata-editor` · `docs(editor): record LevelData editor coverage` | 设计导航、主设计、实施计划、字段矩阵、复盘和 `unity/evidence/`。与 D 盘重复的资料只从当前功能工作树取一份。 |

A–E 分别为 22、1,121、98、19、48 个文件，共 1,308 个可提交路径，另排除 10 个缓存文件。包本体约 105.6 MB。路径清单保存在 D 盘本地审计目录的 `local-commit-batches.json`。清单逐文件归属，保留 Unity 资产和对应的 `.meta`。排除 `.coverage`、`__pycache__/` 等测试产物。用户草稿、恢复数据、Unity 工程缓存和完整原版资源包保存在各自原位置。执行时在当前功能工作树逐批核对暂存路径与提交范围。

## 当前复验与提交前事项

| 验证 | 本次结果 |
| --- | --- |
| Unity EditMode | 仓库脚本完整发现 361 项，353 通过、0 失败、8 跳过。6 项需交互式停靠区，1 项需固定 2.7.81 的 5-10 原始样例，1 项为需另设报告路径的全语料绘制审计。 |
| Python 数据与生成器 | 76 项通过。 |
| Web 数据模型与 PRTSMap 投影 | 4 项通过，外部旧地图语料往返测试 1 项跳过。配置 `MAP_EDITOR_FIXTURE_ROOT` 可运行原来的 44 份样例往返核验。 |
| Git 工作树 | 已跟踪文件的 `git diff --check` 通过。没有实际暂存或提交。 |
| 当前版本安装 | 本次从隔离工程加载本地包并做 EditMode 复验。初期候选的干净 tgz 安装、Player 隔离和 GPU 指标见历史证据。当前版本的干净包安装和 Git URL 安装仍待验收。 |

Unity 复验前共有 4 项失败。本次将测试同步到「地图数据」和「关卡预设」的现行入口，为置换确认注入测试选择并断言实际落格，双原生 SceneView 用例显式检查主窗口停靠条件。外部 Web 样例缺失时，基础回归独立执行。Unity 脚本按实际通过、失败和跳过计数判断结果，已通过本次完整运行。第三方说明已补记现有游戏衍生资料及搜索依赖。

发行前按本次提交内容更新包说明及候选安装记录。完整总图可通过编辑器菜单加载，是否加到 UPM 样例列表留给发行批次决定。成品正文、衍生图标和原生卡框需在包交付清单列明来源。GitHub Actions 当前只记录触发事件，本次本地测试结果单独保存。
