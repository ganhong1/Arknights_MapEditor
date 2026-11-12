# 骨架层 PRTSMap 风格交付 · 2026-11-03

采用分块三维网格、共享无光照程序图案 Shader；每格20顶点/30索引，16×16一块。实际高度、可部署性决定体积/底色；54项本地视觉定义经独立样式映射显示，共用图案不冒充逐ID完整游戏效果。

新增立体/俯视入口、保留原生透视和自由导航，侧面拾取、缝隙排除、未知样式后备。视图不写LevelData，未知字段保留；不会修改原游戏美术资源。

## 验证

独立 AcceptanceProject 全套 EditMode **192/192**通过，覆盖着色器导入/支持、别名、异常字段、网格顶侧面/UV、拾取、脏块/释放、撤销、域重载等已有回归。当前 WorkbenchDemoProject 真实反射核对：MapPreviewStage、1200顶点、Shader supported=true/error=false，工作台和SceneView各1个。未读取截图或进行视觉验收。

首轮188/190：着色器函数名误用HLSL保留字line、旧顶点数量断言仍为每格4；修复后190/190。独立复审补当前stage归属和异常字段后备，再次192/192。测试进程已正常退出，没有在用户演示宿主执行测试。

## 使用

当前工作台的地图示意已刷新。画笔区可选择“PRTS 立体”“2D 俯视”，Alt导航和滚轮缩放使用SceneView原生操作。通过包Samples导入Skeleton Style Gallery，或打开Samples~/SkeletonGallery/level.json查看9×6样式展示；替换当前地图仍走保存提示。示例不是游戏可运行模板。

## 边界

本次未测新GPU性能，不沿用旧几何的性能数字；未验收全地块特有文字/动态黑板状态，洞口为示意图案，路线是控制点连线，装置是编辑框。仿真层将独立持有真实资源/生命周期。未提交、未更新旧final-v2发布压缩包。来源见Editor/Catalog/skeleton-definitions.json与仓库prts-map/SOURCES.md；未新增外部图像素材。
