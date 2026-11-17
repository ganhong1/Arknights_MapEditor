# PR 草稿（未提交）

以下保留初期发行候选的范围和证据。当前目录与源码位置见[项目文件结构](../../../../docs/project-layout.md)，字段编辑能力见[进度树](../../../../ProjectDesignSchemeDocument/LevelData编辑进度.md)。

建议标题：`feat(unity): add LevelData editor package and isolated authoring workflow`

## 问题与变化

现有编辑器主要依赖Web表单与地图视图。新增Unity 2021.3.39f1 Editor-only UPM包，让LevelData文档编辑与SceneView示意操作进入Unity工作流，保留原Web入口。

新增文档解析和局部编辑、引用校验、撤销、原子文件保存、恢复缓存、地形目录及独立 `.arkmap` 美术引用配置。未知LevelData字段保持在文档模型中，示意对象不作为数据真源。核心与存储程序集不依赖游戏代码，编辑器程序集不作为游戏运行组件。

## 范围和限制

- 仅支持已测试的Unity基线；没有游戏连接、自动出战或美术还原承诺。
- 目录64个ID，其中23个完整地块模板；没有装置全量可放置声明。
- 资源映射与游戏生效分开，GUID不写入原生关卡字段。
- 文件冲突使用乐观摘要检测，仍存在跨进程最终检查与替换之间的竞争窗口。
- 候选02已取得干净tgz安装、卸载重装、Player排除和样例导入证据；本草稿仍不是全部完成声明，局部Inspector草稿/原子恢复修复已通过候选03，已补非batch CPU EditorLoop调用测量，GPU呈现帧不在结论中。

## 已取得的候选02证据

- Unity2021.3.39f1，候选02干净tgz测试149/149；总行覆盖82.6%，Core96.1%、Storage92.6%、DocumentSession100%。
- installed/uninstalled/reinstalled三轮Win64 Player构建成功，MapEditor Editor/Tests程序集无Player泄漏，自制场景摘要一致。
- 自制样例导入与回读通过；30×30/60×60/120×120/80×35各5轮同步性能与Undo增长观测完成。另补60×60五轮共500笔：命令p95 19.8717ms、CPU EditorLoop调用p95 25.8167ms；不是GPU呈现帧或输入延迟。
- 准确候选摘要与范围见仓库`unity/evidence/P0_P6_CHECKPOINT_20261103.md`，不能将这些测试计数套到后续改动。

## 候选03最终回归

候选04独立tgz安装179/179通过，总行覆盖86.3%，Core96.1%、Storage93.4%、DocumentSession/InspectorDraftStore100%。恢复v2单文件原子写、v1兼容、旧文档/目标草稿拒绝、取消保存与真实重载均进入测试；最终独立复审无未解决严重/高风险项。

## 最终合并前清单

- [ ] 固定Unity与包版本、提交身份。
- [x] EditMode测试结果与关键失败路径。
- [x] 新代码覆盖率及例外解释。
- [x] SceneView操作、Undo、重载、关闭和恢复验收。
- [x] 候选02自制样例导入、回读与未知字段保留。
- [x] 候选02干净tgz安装、卸载再装和Player程序集排除。
- [x] 性能测量环境、样本规模、CPU p95与退化边界（完整JSON展开/GPU呈现未测）。
- [x] 高风险审查问题全部关闭并附证据。

实际测试数量和结果由集成负责人填写，不能由源代码或文档存在推定通过。提交、推送和PR发布仍需单独授权。

当前后续：经授权提交后Git安装渠道验收；GPU呈现和高级JSON展开性能未纳入本次CPU预算；本地代码与证据已整理，未提交/推送/发布PR。仓库基点 ec480b64130da4a3dbe2a8ca4c6ea72e0f0613b0，新增范围为unity/与设计文档；既有Web改动保留而不混作本次新增。
