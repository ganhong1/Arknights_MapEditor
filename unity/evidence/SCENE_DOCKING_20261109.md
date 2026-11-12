# 新地图原生场景默认停靠

日期：2026-11-09。宿主：Unity 2021.3.39f1，WorkbenchDemoProject。

## 原因与修复

旧创建调用只传入 `typeof(SceneView)`。Unity 的 `CreateWindow` 只匹配具体类型；原生地图场景是子类，主窗口只剩 Game 时也无法匹配，随后执行 `Show()` 生成浮动窗口。

`NativeMapSceneDock` 选择 `ContainerWindow.showMode == MainWindow` 的 Game、Scene、地图场景标签组，调用 `DockArea.AddTab`。只使用 `docked` 不够：浮动多标签宿主也可能返回 true。已在主窗口的场景保持布局；新建地图可替换原浮动视图并保留旧地图的相机状态与文档。停靠失败撤回新增标签和所创建的文档，保留原选择。

API依据：[EditorWindow.cs](https://github.com/Unity-Technologies/UnityCsReference/blob/2021.3/Editor/Mono/EditorWindow.cs)、[ContainerWindow.cs](https://github.com/Unity-Technologies/UnityCsReference/blob/2021.3/Editor/Mono/ContainerWindow.cs)；成员签名另在当前编辑器反射核对。若主窗口没有以上标签组，工作台显示打开 Game/Scene 的提示，不创建浮动场景。

## 验证

最终 EditMode 任务 `4b257cb8defc4c19a081bbd5ae3c9dae`：6项通过、0失败、0跳过。

| 项目 | 结果 |
| --- | --- |
| 存在浮动普通 Scene 时，新场景选择主窗口 Game | 通过 |
| 主窗口内连续新建、原场景复用、关闭后重建 | 通过 |
| 原浮动单标签场景重新停靠 | 通过 |
| 原浮动双标签场景重新停靠 | 通过 |
| 停靠失败，原选择/场景/草稿不变且无新增文档泄漏 | 通过 |
| 两个地图场景的预览场景与相机隔离 | 通过 |

实际点击工作台“新建地图”，生成“未命名地图 3”；场景显示在 Unity 主窗口 Game 旁，`hostMode=MainWindow`。原“未命名地图 2”标签继续保留，更新前与最终恢复副本逐字节一致。最终控制台错误0条，空 HostView 0个，刷新锁已释放。

证据目录：宿主存储根相对路径 `docking-20261109/`；其中 `before-132716.json`、`after-132716.json`、`final-132716.json` 为恢复副本，`tests-final.json` 为最终测试结果。复审发现的浮动多标签判定和失败回滚已修复，并加入上述测试。
