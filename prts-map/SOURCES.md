# PRTSMap 展示适配来源与边界

## 固定来源

- Timeline 本地 `main`：`5396314c3d895a7177f15952d7fdae6ce2806997`。
- `definitions.mjs`、`reference.css`、`blackboard.mjs` 原样取自该提交的 `webui/map/`。
- `editor-renderer.mjs` 复用其 CSS 面结构与地块装饰思路，改为离线 LevelData 编辑；没有引入采集、WS、干员头像或运行态数据链。
- 原始展示来源：https://map.ark-nights.com/ 。Timeline 来源记录指出 CSS 是 CSSOM 快照，定义来自此前本地研究；不据此宣称取得网站资源的再发布授权。
- 本地 `prtsMap-source/analysis/map-rendering-spec.md` 核对了 48px 格宽、2px 间隔、30° 倾斜、900px 透视和底部起算的显示行。
- 本地 `data/levels/obt/main/level_main_05-10.json` 核对了真实预设的 `position/alias/hidden/inst.characterKey` 结构。原始资源仍留在原 D 盘目录，不打包地图全集。

## 实现边界

- 默认 PRTSMap 3D，可切换同一地图的 2D，保留原 Canvas 作为回退。
- 两种 PRTSMap 模式都调用原编辑器的地块操作；投影深拷贝，不写回原生字段。
- `mapData.map` 顶行在前；编辑坐标和预设位置 row=0 在底部，不采用 Timeline 其他旧界面的坐标转换。
- 未知地块保留 ID、黑板、高低地和部署字段，以基础形状和问号表示，不擅自映射成另一种地块。
- `predefines` 和 `hardPredefines` 的 character/token 实例按坐标显示通用标签；隐藏、hard 分别标识，不证明机制激活。卡牌仅计数，不虚构位置。无效位置报告而不夹取到地图边缘。
- 路线是检查点连线，未实现寻路、传送动画或原生战斗模拟。物件专属外观、效果和条件匹配仍需后续实现。
- 所有运行资源为本地相对路径，浏览器与桌面发布清单同步维护。不引入网页远程字体或图片。

## 验证

`node --test prts-map/projection.test.mjs` 验证坐标、原数据保护、未知地块、隐藏/hard 预设、卡牌、无效索引。
浏览器无截图检查了模块/CSS加载、绘制、右键擦除、2D/3D切换不改变导出、Canvas回退和恢复。
桌面资源路由和发布文件清单已更新，未安装 Rust 依赖或执行 EXE 构建，不能将浏览器验证当作桌面验收。

本地主线目录批检：401 张地图、584 个定位预设，投影异常 0。专属样式缺口为 `tile_toxic`、`tile_toxicroad`、`tile_toxicwall`、`tile_toxichill`、`tile_mpprts_enemy_born`、`tile_pollution_roadf`；均保留原数据，不能称为外观或机制已支持。

## 地形中文资料与详情图标

`tile-info.js` 的 23 条资料摘自 BattleLab `docs/reference/terrain-object-reference.md`（2026-11-01，游戏 2.7.81，目录基线 bb8f9ac8db143a661577ed6ef5184d3c6e93d1d0）。说明表未列名的项目明确显示“名称未收录”；高低地/部署副标题来自当前 schema，参考调用条件与当前笔刷分别展示。

详情图标为用户指定素材目录中的 `info.svg`（Feather info 样式），原样复制为 `assets/icons/info.svg`，不引入绝对路径或远程图标依赖。
