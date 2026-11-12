# MapData 编辑入口与职责方案

日期：2026-11-09。状态：用户已批准，源码已实现，定向测试与原草稿保全核验已完成；测试窗口已清理，刷新后的 map/mapData、tiles 与 options 界面已核对。分批通过记录见[本轮验证证据](../unity/evidence/MAPDATA_OPTIONS_EDITOR_20261109.md)，不把编辑器验证等同于游戏验收。options 继续按用户认可的原始声明逐字段编辑，本模块不改变该结构。

## 结论

已将原 TileData 入口升级为独立的「地图数据 · mapData」按钮，承接现有 TileData 编辑和地图尺寸；全局设置仅保留独立的 options 编辑。mapData 不只是地图宽高，而是网格索引、地块配置及地图级附加记录的完整容器，直接关联绘制、配置共享和空间引用。

现有 TileData 入口已成为 mapData 模块内独立的 `tiles · TileData` 子编辑器，复用现有功能、草稿与事务，不建立第二套 TileData 管理。原工作台页 ID 6 保留，子页默认选中 tiles；若后续保留快捷入口，只定位到同一个 `mapData.tiles` 页面。右侧资源库地形不变，继续提供放置来源。

## 原始数据结构与界面

```text
LevelData
├─ options                       → 全局设置中的独立 options 页面
└─ mapData                       → 地图数据按钮
   ├─ map                        short[,]；JSON 中为二维数组
   │  ├─ 尺寸摘要和调整尺寸操作   界面工具，不增加 JSON 字段
   │  └─ [row][col]              指向 tiles 的配置索引
   ├─ tiles                      TileData[]
   │  └─ [index]                 复用现有 TileData 编辑与多格绑定
   ├─ blockEdges                 Edge[]
   │  └─ [index]
   │     ├─ pos                  GridPosition
   │     ├─ direction            SharedConsts.Direction
   │     └─ blockMask            MotionMask
   ├─ tags                       string[]
   ├─ effects                    MapEffectData[]
   │  └─ [index]
   │     ├─ key                  string
   │     ├─ offset               Vector3
   │     └─ direction            SharedConsts.Direction
   └─ layerRects                 string[]
```

模块内按以上六字段的声明顺序提供导航，另有「完整 mapData JSON」入口。选中字段显示对应编辑区，切换只改变显示，不重建子编辑器；当前路径始终可见。tiles 保留独立的原有配置管理，map 提供尺寸控件和二维数组 JSON，其余四字段目前提供原始 JSON 编辑，不宣称已有逐项结构化表单或图形编辑器。中央 LevelData 字段树继续显示已应用文档，不额外建立一份字段树作为数据来源。

`MapData.width/height` 是标记了 JsonIgnore 的只读属性，不是序列化字段；JSON 长宽来自 `map` 的行列。界面的「调整尺寸」已移入 `map` 字段内，不导出 width/height，不写入 options。宽高的未应用输入保存在独立的 `mapData:dimensions` 编辑器草稿中，该恢复记录不是 LevelData 输出字段。

## 各字段的编辑边界

| 字段 | 当前源码实现 | 明确边界 |
|---|---|---|
| map | 宽高输入、当前尺寸摘要、裁剪预检操作及二维数组原始 JSON；场景绘制继续复用 | 每格为 tiles 索引；显示坐标与 JSON 行序的转换由现有坐标层负责 |
| tiles | 复用现有配置列表/块状浏览、增删改查、多格绑定及三选项确认 | 共享配置影响全部引用格；删除条目必须同步重映射索引 |
| blockEdges | 原始 JSON；帮助说明 pos/direction/blockMask 结构 | 字段声明已知；尚无逐字段表单，枚举、坐标方向和阻挡消费规则需核验后开放图形助手 |
| tags | 有序字符串数组的原始 JSON，保留未知值 | 尚无逐条列表控件；不凭名称发明枚举或效果解释，也不自动去重 |
| effects | 原始 JSON；帮助说明 key/offset/direction 结构 | 尚无逐字段表单；与 tiles[n].effects 属于不同路径，不是 MapGraphic/Unity 场景的全部美术配置；资源与运行表现未验证 |
| layerRects | 有序字符串数组的原始 JSON | 尚无逐条列表或矩形绘制器；具体字符串格式、空间语义与合法边界未核验 |

各原文编辑区显示已应用字段的缺失、null、类型及条目数。四个可选字段提供设为空数组、设为 null、移除字段三个草稿操作，显式应用后才写入文档。空白草稿表示移除可选字段，不与 JSON null 混淆；必需的 map/tiles 缺失仍由文档结构校验拒绝。未知字段保持；本次改动到的已知可选集合校验数组及条目基本类型，未改的异常旧值不阻塞无关字段编辑。类型声明不是完整业务值域证明。

## 数据一致性与尺寸变更

1. `map` 与 `tiles` 共用一个文档事实来源。涉及索引重映射的动作一起提交、一起撤销；不能分别保存导致格子引用失效。既有共享配置、显式独立条目身份和清理确认契约继续有效。
2. 当前 C# `LevelDocument` 校验矩形网格及 tiles 引用合法性；新 mapData 专用应用契约进一步校验引用索引不超过原生 short 的 32767。未把这一局部增强描述为所有旧导入和其他编辑入口都已完成原生范围审查。
3. 当前 Resize 主要重建 map，保留左下角坐标；JSON 行序从上向下，界面坐标从下向上。扩展时追加一条供新增格共享的填充 TileData，收缩时保留原 tiles；不自动去重、合并或清除失去引用的配置。收缩预检扫描具有 row/col 的对象，拒绝裁去已识别的引用坐标。现有 1..256 尺寸限制是编辑器策略，不是已证明的游戏上限。
4. 这项通用扫描不能证明覆盖 blockEdges 的全部几何关系、effects.offset、layerRects 字符串或其他机制引用。不能宣称现有裁剪预检已验证全部 mapData。
5. 当前尺寸页显示已应用尺寸以及 blockEdges/effects/layerRects 的未验证空间语义提示；裁剪预检拒绝已识别的失效坐标并返回具体数据路径。完整受影响格子/引用清单仍属后续增强，尚未实现。未闭合格式保留原文，不自动平移、删除、钳制或重写；未知语义不能标为安全。
6. routes、predefines 等虽在 mapData 外，也可能引用地图位置。模块拥有 mapData 编辑职责，地图级审查可以报告跨模块影响，但不能因调整尺寸就静默改写其他模块。
7. 子编辑区和原始 JSON 均绑定文档身份与版本，复用一个 mapData 工作区及现有 TileData 子编辑状态。TileData 原草稿槽及 tile:N 目标保持，新增 `mapData:字段`、`mapData:root` 和尺寸草稿槽。专用 mapData 应用会拒绝覆盖尚未处理的 TileData 草稿或另一份 mapData 草稿；干净字段随文档刷新，脏草稿保留并由版本检查防止过期提交。多份未应用草稿须先逐一处理，显式重新读取沿用丢弃确认，不自动合并。恢复、编辑锁与跨模块不误写已纳入本轮定向测试；当前窗口已核对；全部尺寸及切图组合的体验覆盖另作增强。
8. 新 mapData 专用原文应用使用 WithMapData/WithMapDataField；尺寸操作先 Resize，再通过相同 MapData 契约。JSON 缩图复用 Resize 的 row/col 裁剪检查，并检查替换中新带入的相关坐标。原有通用 WithSection("mapData") 仍不能单独当作完整空间预检；仅能解析成功不代表空间引用有效。

## 与其他入口的关系

- 全局设置：options 原始15字段保持独立，按用户已认可方案实施。
- 地图数据：六字段及其派生的尺寸操作，作为一个独立模块。
- 资源库地形：选用原版 TileData 配置来源，不是第二份当前地图配置表。
- 场景窗口：map/tiles 的空间编辑视图，写入同一文档。
- 中央 LevelData 树：已应用文档的完整浏览与定位，不维护另一个副本作为事实来源。
- 美术模块：可对 mapData.effects 提供同源快捷编辑，但不能另外储存一套相同字段；Unity 场景和 MapGraphic 的独立资源配置不强塞进 mapData。

## 实施与验证状态

- 已获用户批准：独立 mapData 模块，内部保留 TileData 独立性，尺寸归入 map，全局设置保留 options。
- 已实现并完成定向验证：六字段导航、只复用一个原有 tiles 子编辑器、map 尺寸操作、四个可选字段和整体 mapData 原文编辑、缺失状态及草稿保护。
- 已完成 MapData 专用 Core 契约和 UI 定向测试：原生索引范围、严格 JSON 边界、裁剪入口一致性、坐标方向、原子操作、过期整体JSON拦截、跨模块不误写、撤销、编辑锁及 TileData 草稿跨页面/重建保留。
- 联合 options 的首轮67项中64项通过；修复嵌套 JArray 测试构造、旧绘制确认替身、旧控件查询三项后，新增1项 Options 单字段JSON回滚测试，定向补测13/13通过。合并68个唯一测试均有通过记录，不是单次68项；零项错误筛选任务不计数。详见[本轮验证证据](../unity/evidence/MAPDATA_OPTIONS_EDITOR_20261109.md)。
- 原10×6文档、完整JSON草稿、SavedJson、ProjectJson、SourcePath及原非options局部草稿前后一致；仅干净 options 缺失占位同步为真实 missing 表示，没有改变文档。
- 已完成最终工作台刷新与原地图界面核对、草稿比对和测试窗口清理。修正新增测试的窗口关闭方式后18/18复验通过，空宿主0个；不重复累计唯一测试数。全部窄窗口与多地图切换组合不属于本轮穷举验收。
- 后续增强：逐项可选字段表单、完整受影响清单、更深入的空间引用审查；在语义闭合后再增加阻挡边、效果和区域图形编辑。源码与编辑器验证不代替游戏运行验收。

## 本地证据

- APW `source/ark-mix-source/DumpCS/2.7.81/dump-pc-x64-inspector-20261026.cs`：MapData 第662823行，字段第662826行起；width/height 第662834行起；Edge 第662843行起。MapEffectData 第663016行，字段为 key/offset/direction。
- 插件 `unity/Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Core/LevelDocument.cs`：构造时检查 map/tiles；GetCell 映射底部起算坐标；Resize 重建二维数组及进行 row/col 裁剪预检。
- 插件 `Editor/Core/LevelDocument.MapData.cs`：专用字段/整体应用、Int16 索引范围、可选集合基本形状校验及空间警告；`Editor/UI/MapEditorWindow.MapData.cs`：六字段导航、尺寸、原文和草稿应用（均相对上述 UPM 包目录）。
- 插件 `Editor/Core/LevelDocument.TileConfigurations.cs`：配置删除重映射索引及绑定事务；`Editor/UI/MapEditorWindow.TileData.cs`、`Editor/Application/InspectorDraftStore.cs`：完整文档版本与 tile:N 草稿检查（均相对上述 UPM 包目录）。
- 既有共享与绑定契约见 [Unity 编辑器设计](UNITY_EDITOR_DESIGN.md) 的「TileData 实际编辑事务」和「TileData 单一编辑入口」。
