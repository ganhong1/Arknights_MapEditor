# Unity 原生 PRTSMap 逐项视觉核对

最新总览已扩为72×34，菜单改为`Tools/Arknights/PRTSMap/Complete Catalog Overview`。原54＋685保留在左侧，右侧66地块展示项/371物件占位。总计120地块展示项、1056物件，不把3模式别名或1虚拟箱子项称为实际地块Prefab。占位黄问号与粉色叉只是缺图/缺样式提示；悬停显示名称与ID。`prts-overview-expanded-01`197项通过，685原生图标＋371占位边界已核对，当前窗口以可撤销命令更新。

全量总览入口：`Tools/Arknights/PRTSMap/Complete Overview (54 + 685)`。样本`Samples~/CompletePrtsGallery/level.json`为36×34，`index.json`记录739项坐标与ID。顶部三排54个地块、下部685个物件；未用地块作为间隔，不把间隔重复计入样式。地图数组按顶行在前存储，坐标索引与预设按底行row=0；新增回归检查两者一致。`prts-overview-03`197/197通过，Unity编译无错误；打开前后原有2个窗口JSON一致。原生捕获图位于D盘验收目录`prts-complete-overview.png`，不作为逐项像素一致签收。

当前验收未完成。固定参考来自本仓 prts-map，使用同一 LevelData、显示模式和画面尺寸。每项须包含几何、底色、符号、文字和叠层对照，测试通过不替代视觉验收。

| ID | 参考文字 | 效果 | 视觉签收 |
| --- | --- | --- | --- |
| tile_forbidden |  | 基础地块 | 待逐格签收 |
| tile_floor |  | 基础地块 | 待逐格签收 |
| tile_wall |  | 基础地块 | 待逐格签收 |
| tile_road |  | 基础地块 | 待逐格签收 |
| tile_start |  | 基础地块 | 待逐格签收 |
| tile_flystart | air | 基础地块 | 待逐格签收 |
| tile_end |  | 基础地块 | 待逐格签收 |
| tile_telin | in | 基础地块 | 待逐格签收 |
| tile_telout | out | 基础地块 | 待逐格签收 |
| tile_hole | hole | 基础地块 | 待逐格签收 |
| tile_empty |  | 基础地块 | 待逐格签收 |
| tile_grass |  | effect_grass | 待逐格签收 |
| tile_fence |  | 基础地块 | 待逐格签收 |
| tile_fence_bound |  | tile_fence | 待逐格签收 |
| tile_healing |  | effect_healing | 待逐格签收 |
| tile_bigforce | F+ | effect_buff | 待逐格签收 |
| tile_defup | def+ | effect_buff | 待逐格签收 |
| tile_gazebo | air+ | effect_buff | 待逐格签收 |
| tile_infection |  | effect_infection | 待逐格签收 |
| tile_volcano |  | effect_volcano | 待逐格签收 |
| tile_volcano_emp |  | effect_volcano | 待逐格签收 |
| tile_corrosion |  | effect_corrosion | 待逐格签收 |
| tile_defbreak |  | effect_corrosion | 待逐格签收 |
| tile_shallowwater |  | effect_shallowwater | 待逐格签收 |
| tile_deepwater |  | effect_deepwater | 待逐格签收 |
| tile_deepsea |  | effect_deepwater | 待逐格签收 |
| tile_volspread |  | effect_volspread | 待逐格签收 |
| tile_smog |  | effect_smog | 待逐格签收 |
| tile_wooden_wall | WW | 基础地块 | 待逐格签收 |
| tile_yinyang_road |  | effect_yinyang_road | 待逐格签收 |
| tile_yinyang_wall |  | effect_yinyang_wall | 待逐格签收 |
| tile_yinyang_switch |  | effect_yinyang_switch | 待逐格签收 |
| tile_poison |  | effect_poison | 待逐格签收 |
| tile_icestr |  | effect_ice | 待逐格签收 |
| tile_icetur_lb |  | effect_ice | 待逐格签收 |
| tile_icetur_lt |  | effect_ice | 待逐格签收 |
| tile_icetur_rb |  | effect_ice | 待逐格签收 |
| tile_icetur_rt |  | effect_ice | 待逐格签收 |
| tile_aircraft |  | 基础地块 | 待逐格签收 |
| tile_reed |  | effect_reed | 待逐格签收 |
| tile_reedf |  | effect_reed | 待逐格签收 |
| tile_reedw |  | effect_reed | 待逐格签收 |
| tile_mire |  | effect_mire | 待逐格签收 |
| tile_passable_wall |  | 基础地块 | 待逐格签收 |
| tile_passable_wall_forbidden |  | 基础地块 | 待逐格签收 |
| tile_stairs | ↕ | 基础地块 | 待逐格签收 |
| tile_grvtybtn |  | 基础地块 | 待逐格签收 |
| tile_ristar_road |  | effect_ristar_road | 待逐格签收 |
| tile_ristar_road_forbidden |  | effect_ristar_road | 待逐格签收 |
| tile_start_cooperate |  | tile_start | 待逐格签收 |
| tile_end_cooperate |  | tile_end | 待逐格签收 |
| tile_rcm_operator |  | tile_road | 待逐格签收 |
| tile_rcm_crate |  | tile_road | 待逐格签收 |
| tile_crate |  | effect_crate | 待逐格签收 |

本轮修正箱子上下边缘的金色菱形，文字根据 content_on_top 定位。网页图片仅用作对照，产品继续使用 Unity 原生网格与 Shader。

## 全量资源接入增量

685个物件已进入原生图集Mesh渲染，逐ID覆盖表见[物件覆盖](PRTS_OBJECT_COVERAGE.md)。`prts-objects-03`隔离Unity EditMode测试196/196通过；覆盖每个图集区域、685实体几何、隐藏/未知/卡牌边界、文档不改写、图标与字形网格释放。地块字形改为平面Mesh；CSS向下坐标与UV向上坐标已修正；当前本地参考实测字体13px。阴阳切换白圆36px/黑圆16px根据固定CSS修正。

视觉边界仍包括逐项最终签收、非token方向指示、动态动画与任意视角透明排序；不以本报告宣称像素完全一致。
