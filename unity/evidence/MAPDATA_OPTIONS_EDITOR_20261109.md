# MapData 独立模块与 Options 字段表单验收

日期：2026-11-09。运行环境：Unity 2021.3.39f1，WorkbenchDemoProject。本轮完成编辑器实现、定向测试、现有草稿保全及刷新后的界面核对，不涉及游戏运行或私服操作。

## 实现范围

| 入口 | 实际实现 | 保留的契约 |
|---|---|---|
| 地图数据 · mapData | map、tiles、blockEdges、tags、effects、layerRects 六个子模块及整体原始 JSON | 统一文档与事务；未知成员保留 |
| map | 地图尺寸、二维索引数组编辑 | 宽高由数组行列决定，不增加 width/height JSON 字段；复用裁剪预检 |
| tiles · TileData | 原有独立配置管理、列表/块状、增删改查、多格绑定 | 仅一个原 TileData 编辑器，继续使用 tileData 草稿；共享修改和三选项确认不变 |
| 其余四个 mapData 字段 | 原文编辑、基本类型校验、missing/null/[] 区分 | 未改变的旧异常保留；未知空间语义提示，不自动重写 |
| 全局设置 | 只编辑 LevelData.options 的15个字段，按原声明顺序 | 与完整 options JSON 共用 global:options 草稿；局部提交，一次撤销 |
| functionDisableMask | 19个已知标志的选择与检索 | 未知位保留；未修改时保留原整数或字符串表示 |
| configBlackBoard | 有序列表及详情；新增、复制、删除、上下移动、key/value/valueStr | 重复键、大小写、未知成员及缺失/null/空列表不合并 |

单字段非法 JSON 现在明确回滚显示并报错，避免输入看似被接受、实际沿用旧草稿。完整 options JSON 的非法内容仍作为草稿保留。导入旧文档时不自动创建 options 或补入参考初值。

## 测试与修复

| 任务 | 结果 | 说明 |
|---|---|---|
| 3d8b44d73f0a4e5e9b447dfb3397cad9 | 67项，64通过，3失败 | MapData/Core/UI、Options、TileData、局部草稿、窗口、编辑流程相关组 |
| 675d778bbc1d41928792bf737afb4351 | 13/13通过 | 10项Options（含新增1项）及3项失败修复 |
| 47b0fde561cc41a5a64040eb7bccbb64 | 18/18通过 | 新增两组窗口测试改用Close后，专门复验功能和窗口释放 |

首轮三项失败分别是嵌套 JArray 测试数据构造错误、旧绘制测试未提供三选项确认替身、旧测试查询已移除的地形编辑控件；全部已修正并有通过记录。旧控件测试迁移到当前 TileData 编辑器，继续检验未知字段、非法草稿与放置保护。

合并覆盖68个唯一测试场景均有通过记录，**不是单次运行68项**，后续18项复验不重复累计。错误地同时限定 test_names/group_names 的任务 f7e8af59d043461a92e599daed4dd6ac 执行0项，不算通过。

新增窗口测试先清理自身草稿、DiscardChanges，再调用 EditorWindow.Close；不用 DestroyImmediate 留下原生空窗口。清理已产生的测试空宿主及测试专用窗口后，18项复验结束时 HostView 共6个、空宿主0个。原工作台、原文档编辑器、原地图 SceneView 均保留。最终控制台错误0条，自动资源刷新已恢复。

## 草稿保全

在原编辑器实例上生成更新前与最终恢复副本并逐字段比较：

- DocumentJson、DraftJson、SavedJson、SourcePath、SourceHash、ProjectJson 全部相同。
- 原 action、bgmEvent、environmentSe、preset、route、routePoints、section、tileData 局部草稿全部相同。
- 原 global:options 是无改动的旧缺失占位 `null`；新面板依据实际文档同步为缺失的空字符串。文档没有写入 options，也未丢弃脏草稿。
- 新增7个干净 mapData 草稿槽；未覆盖原 TileData 槽。当前原地图仍为10×6。

恢复副本SHA-256：

| 文件 | SHA-256 |
|---|---|
| precompile--1458.json | 348b776e017fd8442d89fa0a3d2cfcb538fef9203444766bd342778383aa578f |
| final--1458.json | d0d173de248ea3d16bbb935dee672aafa3a4e9387428cf7bb9492225c7e33197 |

外部证据目录为 `storage/map-editor-unity/mapdata-options-20261109/`，包含恢复副本、`draft-preservation-final.json`、`test-first.json`、`test-final-full.json`、`test-window-cleanup.json`。`test-first-full.json` 是域重载后失效任务的查询，不作为测试结果；首轮以已保存的 `test-first.json` 为准。

## 界面核对与剩余边界

刷新后实际查看 mapData/map、mapData/tiles 和全局设置/options：模块可见、尺寸位于 map、tiles 保持独立编辑，options 缺失时展示字段结构及明确创建入口。原 SceneView 与工作台并排显示；最终停留在 tiles。未修改地图内容或相机参数。

本轮不是所有窗口尺寸、全部长列表和多地图切换组合的穷举体验验收。四个可选 mapData 字段目前是原文编辑；更完整的空间引用分析、图形编辑、options 黑板逐键助手和更深的原生值域校验仍属后续增强。不能将已检查的 row/col 裁剪扫描或字段类型校验解释为完整运行合法性证明。

源文件摘要见 [来源清单](MAPDATA_OPTIONS_EDITOR_20261109.sources.json)。设计：[MapData](../../ProjectDesignSchemeDocument/MAPDATA_EDITOR_DESIGN.md)、[Options](../../ProjectDesignSchemeDocument/OPTIONS_EDITOR_DESIGN.md)。
