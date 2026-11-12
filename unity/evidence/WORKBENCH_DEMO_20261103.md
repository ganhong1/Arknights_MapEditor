# 原生工作台体验 Demo

2026-11-03，基于现有UI Toolkit重组，非最终视觉验收。

- 资源库和属性双栏，可调整分隔；默认仅显示23个有配置模板的地形，中文名和ID分层，其他目录项可显式展示。
- 地形、路线/装置、波次、美术、高级页签；高度/部署/通行独立控件，保留未知JSON字段，损坏草稿不覆盖。
- 高级JSON默认折叠；原生SceneView保留导航和绘制。demo并排放置工作台与SceneView，未使用私有停靠API。
- 新增USS样式；现有保存/恢复/Undo逻辑复用。
- 修正MapPreviewStage的OnEnable/OnDisable覆盖及base调用，确保Stage注册/释放遵循Unity2021生命周期。交互测试曾因Stage导航错误被中止，不记通过；原缓存已备份，体验迁到独立WorkbenchDemoProject。
- 独立AcceptanceProject工作区代码回归180/180、零失败；体验宿主1工作台/1SceneView，MapPreviewStage有效，无控制台错误。没有截屏验收。
- 交付tgz仍是此前版本；demo仅在工作区，不把旧覆盖率/性能数字套到新界面。未提交推送。
