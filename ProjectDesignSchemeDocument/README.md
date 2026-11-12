# 地图编辑器设计与实施导航

参照APW的文档职责分层：上层维护稳定设计和技术基线，下层按方向保留唯一主实施计划。设计批准、代码实现、编辑器验收、游戏验证和Git交付分别记录。

| 文档 | 职责/当前状态 |
| --- | --- |
| [当前设计、代码与本地提交批次](LOCAL_COMMIT_REVIEW_20261112.md) | 两工作树、五个分支、提交路径分组、验证和发行待办 |
| [LevelData 字段与编辑器落地](LEVELDATA_EDITOR_FIELD_MATRIX.md) | 22 个原生顶层字段，Options、MapData、TileData 与预设编辑细节 |
| [地图美术代码与资源研究](ART_RESOURCE_CODE_AND_ASSET_RESEARCH.md) | 原版配置与加载链、MapGraphic和TileData关系、资源路径与对象引用、当前插件状态及接入提议 |
| [Unity美术分层抽样](../unity/evidence/ART_RESOURCE_SAMPLING_20261109.md) | 原资源包处理、纹理/材质/模型/动画/场景实际渲染、光照对照、脚本及拖尾边界 |
| [5-10 美术组装与预览](BATTLE510_ART_ASSEMBLY.md) | 依赖补齐、原场景、红蓝门、弩炮、Game 视图稳定性与释放 |
| [Options编辑设计](OPTIONS_EDITOR_DESIGN.md) | 已接15字段表单、掩码和内嵌黑板，保留同槽草稿；验收见主计划 |
| [MapData入口设计](MAPDATA_EDITOR_DESIGN.md) | 已迁移为独立六字段模块，tiles复用原TileData，尺寸归入map；验收见主计划 |
| [Options值域调查](OPTIONS_VALUE_RESEARCH_20261106.md) | 15字段与固定原生消费者；FP阈值已纠正为EN5，业务缺口单列 |
| [Options黑板逐键约束](OPTIONS_BLACKBOARD_CONSTRAINTS_20261106.md) | 89观测键，79部分消费者/10仅样例，不是完整允许键总表 |
| [UNITY_EDITOR_DESIGN](UNITY_EDITOR_DESIGN.md) | 已批准架构、数据/生命周期契约、D01–D08取舍及产品边界 |
| [TECHNOLOGY_STACK_BASELINE](TECHNOLOGY_STACK_BASELINE.md) | Unity2021.3.39f1、UPM/依赖、测试和测量口径 |
| [Unity主实施计划](PlanDocumentsForImplementation/UnityEditor/UNITY_EDITOR_IMPLEMENTATION_PLAN.md) | P0–P6队列与后续模块增量，候选04 的 CPU 和整个 Editor 的 GPU 测量见历史记录，Git 安装另行验收 |
| [最新交付与证据](../unity/evidence/UNITY_DELIVERY_20261103.md) | 候选04的179/179、覆盖率、包摘要及明确未验收项 |
| [模式与全量正文核验](../unity/evidence/LEVELDATA_MODE_AUDIT_20261105.md) | 3,899 份正文、原生正文映射、主线 352 条关系与明确开放问题 |
| [装置配置与出处核验](../unity/evidence/DEVICE_PRESET_AUDIT_20261106.md) | 3,899 份正文、403 个预设 ID、场上／卡牌分别去重与全部出处 |
| [装置资源库接入](../unity/evidence/DEVICE_LIBRARY_20261106.md) | 847 项目录、推荐／全部配置／出处浏览与草稿选用，Unity 34 项定向通过 |

维护规则：稳定定义进入上层设计，阶段状态只以主计划和版本化证据为准。旧P1检查点保留历史，不能覆盖更新候选的事实。源码与证据采用相对路径；机器安装和缓存路径通过本地配置注入，不进入发行依赖。
