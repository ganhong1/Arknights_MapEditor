# 冻结目录来源

2026-11-03 从本 feature 工作区提取；具体字节 SHA-256 位于 `catalog.json.sources`。路径相对于原地图编辑器仓库根。游戏加载、运行效果和完整目录覆盖不由本文件证明。

| 来源 | 提取范围 | 数量/限制 |
| --- | --- | --- |
| `schema.js` 的 tiles | 完整地块模板原样复制 | 23 个；含黑板与效果字段，不以视觉定义补默认值 |
| `tile-info.js` | name、description | 23 个；不对未收录条目猜中文名 |
| `prts-map/definitions.mjs` 的 tiles | ID 及是否有示意定义 | 54 个；没有复制 CSS 类或把样式当资源模板 |
| `prts-map/projection.mjs` | 预设/卡牌分类结构参考 | 不是装置静态索引，不包含完整装置模板 |

初始去重并集为 64 条。家族前端以固定 Android 地块 Prefab 索引补齐到 116 个原生键，并保留 1 个骨架箱子示意项。家族关系来自设计文档的首版分类，补充名称与说明来自 BattleLab 地块参考。模式说明别名单独保留在参考材料中。早期 23 份模板保留在目录基线中。Unity 资源库的直接选用和详情配置现由原版样例索引提供。

`DocumentObjects` 从用户当前 LevelData 读取 predefines/hardPredefines：characterInsts、tokenInsts 与 characterCards、tokenCards 分离；hidden 标记单独分类。waves/branches 中 ACTIVATE/TRIGGER/WITHDRAW_PREDEFINED 动作独立标记为 native-action。这表示文档输入类别，不代表已证明某 ID 的完整机制。未知ID与原JSON保留，不将动作key转换为装置模板。

目录按 ID 排序。模板仅取原有 schema.tiles；中文名优先使用 tile-info，缺失时使用原版说明索引中的已收录项；无模板写 null、无中文写空字符串。家族字段仅写已确认的 43 个成员。详情按钮使用仓库 assets/icons/info.svg 栅格化的本地副本。重新生成时复核数量并更新来源摘要。

## 原版 TileData 配置样例

`terrain-samples.json` 取自本地 ArknightsGameData `bb8f9ac8db143a661577ed6ef5184d3c6e93d1d0` 的 `zh_CN/gamedata/levels`。3,898 份关卡中实际被地图格引用的配置，按具体地块键和完整 JSON 内容去重。对象键的顺序不参与去重，数组顺序及缺失、空值、空数组各自保留。每项保留所有关卡相对路径，并记录其中一份的 `mapData.tiles[]` 索引。

共 1,120 套样例，覆盖 111 个地块。缺样例的是 `tile_alchemy_road`、`tile_alchemy_wall`、`tile_empshock`、`tile_pollution_wallf`、`tile_shallowwater`。默认推荐从原版样例中选出最多三项，依次考虑字段是否齐全、黑板与特效项数量、出现关卡数及稳定排序。第一个推荐是资源库直接选用的配置，完整样例可在详情中显式选用。这里的推荐是插件的选择。

通过 `unity/verification/build-terrain-samples.py` 重新生成，传入关卡目录、目录 JSON 和输出路径。关卡目录必须与记录的 Git 修订一致。

同一固定快照的 `terrain-source-maps.json` 保存只读地块平面投影。生成器 `unity/verification/build-terrain-source-maps.py` 逐格核对完整配置，并验证每份配置的出处集合和样例目录一致。预览青色格对应当前选中的完整 TileData 配置。

## 原版装置配置样例（2026-11-06）

`device-index.json` 与 `DeviceSamples/*.json` 来自同一游戏数据提交的装置普查。权威证据 `device-presets.json` SHA-256 为 `c73cfeaa2ecd1b696bca6208d224f229eb9b94a4733ad14baaf4116057a66f79`。403 个 ID 的 20,694 条记录分别按 tokenInsts/tokenCards 提取，schema v3 仅排除场上 position/direction，整理为 6,919 份场上配置和 811 份完整卡牌配置；出处完整保留 14,003 份原记录及所有原数组位置。常规／困难预设指针保留，不据其存在声明启用。

目录含历史 846 项和原版预设中额外出现的 `token_10053_radian_tower3`，共 847 项。旧目录独有的三项仅保留名称和 ID，没有伪造配置。默认推荐最多三份配置，属于插件按参数、字段完整度和出处覆盖选取的原版样例，不是原生默认或独立可用证明。

离线生成使用仓库 `tools/build_device_library.py`，显式传入权威证据、同哈希 character_table、历史目录差异和新输出目录。生成后用 `tools/enrich_device_search.cjs` 传入本地 AngelinaTravelToolbox 目录及索引路径，以既有 pinyin-pro 补充拼音／首字母。运行查询继续复用现有 Fuse.js 7.5.0、Jint 和 Bitap 对照实现，不引入新的搜索依赖。目录索引约 345 KB，详细配置按装置拆分为 403 份文件，按装置读取且有界缓存。

出处预览显示对应的原始装置配置及 JSON 数组位置，不启动游戏，不自动迁移配套分支或波次，不进行装置实际效果验收。
