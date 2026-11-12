# Unity 插件本地交付与验证边界

日期：2026-11-03。分支 `feature/unity-leveldata-editor`，Git 基点 `ec480b64130da4a3dbe2a8ca4c6ea72e0f0613b0`。全部新增仍在工作区，未提交、推送或发布 PR。

## 已实现范围

| 阶段 | 成果 | 当前结论 |
| --- | --- | --- |
| P0 | Web 文件与样本摘要、包身份、来源和迁移边界 | 已记录，保留原 Web 改动 |
| P1 | 严格 JSON、未知字段及数值保留、导入导出、Undo、冲突检测、原子保存、恢复 | 候选04通过；完整/局部/美术草稿统一v2恢复，兼容v1 |
| P2 | 1–256可变长宽、底部原点、分块示意、绘制/擦除/拾取/填充/裁剪预检、目录和预设输入 | 编辑闭环已通过；64个目录ID、23完整模板，不是全量游戏资产支持 |
| P3 | 路线/检查点/引用重排、敌人、波次/分支/动作、预设/隐藏/卡牌和其余原生JSON | 固定Web差分、未知字段和拒绝破坏性修改用例通过 |
| P4 | 独立`.arkmap`、相对路径、Unity GUID映射、缺资源诊断、可移动工程 | 配置保存已通过；不构建或注入游戏资源 |
| P5 | 干净tgz安装、真实重载/Play Mode、恢复失败、覆盖率、卸载重装、Player排除、独立审查 | 工程验收通过；60×60 CPU EditorLoop调用p95 25.8167ms，GPU呈现未测 |
| P6 | 上层设计/技术基线/计划、使用说明、PR草稿、验收脚本与证据 | 审核材料已形成；Git安装渠道和发布未执行 |

复杂字段允许通过校验后的原生JSON编辑，不把“有编辑入口”说成完整游戏机制模拟。美术映射保存在编辑工程，不混入LevelData或自动替换用户场景。

## 精确验证

- 固定 Editor：Unity **2021.3.39f1 / Windows**。UPM依赖Newtonsoft3.2.2；测试框架1.1.33。
- 候选04归档SHA256：`a624c58972addc7a40560b200462485e667237e29917a76faed906ec66649758`。
- 独立AcceptanceProject从tgz安装，**179/179通过，0失败/0跳过**。包含真实编译域重载、Play Mode生命周期、窗口输入和导入/编辑/Undo/保存/重开。
- 总行覆盖 **86.3%（1815/2101）**；Core96.1%，Storage93.4%，Editor78.6%，DocumentSession/InspectorDraftStore100%。没有分支覆盖数据。Editor未覆盖部分包括原生文件对话框和SceneView绘制分支，不能因整体覆盖达标称这些路径全部测过。
- 此前候选02实际完成安装→卸载→重装三轮Win64 Player构建；候选03及04各另完成一次安装后构建。五次自制场景摘要相同：`1c4d70c2b972b446516a2dc7324ccde46c44e51f5c847245fe5aa6afef537e17`。MapEditor及其Tests程序集未进入Player。
- 通过Package Manager公开Sample API导入自制样例，2×2数据编辑/保存/重开保持未知字段，见[sample证据](sample-import-20261103.json)。
- 最终独立复审无新的可复现严重/高风险缺陷。已修复局部草稿错误目标应用、分文件恢复不一致、取消保存继续保存工程、工程切换旧绑定和损坏坐标恢复部分应用。
- 最新机器可读结果：[候选04](acceptance-candidate04-20261103.json)；此前卸载重装及性能：[候选02](acceptance-candidate02-20261103.json)。计数有重叠，不累加。

## 本地包

`mapeditor-0.1.0-preview.1-final-v2.tgz`，SHA256：`2b8bb268ab2648f7a64628a79a860489919ccaeb0962ea66241a1877ca781956`。

包位于外部D盘验收区`map-editor-unity/package-audit/`。152文件，代码、测试、依赖及资源与已测候选04完全相同，只有两份Documentation文档更新了验收结果。见[逐文件校验](release-v2-audit-20261103.json)。没有游戏二进制资产、Library或本机依赖路径。

安装：Package Manager → Add package from tarball，或从本仓库`unity/Packages/io.github.lingrs-llm-researchteam.mapeditor/package.json`添加本地包。入口：**Tools → Arknights → Map Editor**。

## 剩余验收及限制

1. **已完成CPU连续编辑测量，GPU呈现/输入延迟仍不在验收范围。** 非batch、D3D11、i7-12700KF/RTX4060Ti、60×60、五轮各100笔、每轮100次实际SceneView重绘；500原始样本独立复核通过。命令p95 **19.8717ms**、CPU EditorLoop单次调用p95 **25.8167ms**，各轮后者最大31.0367ms，低于33ms预算。优化前为38.9749/55.8755ms。默认折叠完整JSON面板；展开大文本的性能未测。此前30/60/120及80×35同步测试仍仅代表同步路径。见[优化前原始报告](interactive-performance-05-20261103.json)、[优化后原始报告](interactive-performance-06-20261103.json)。不得改写成屏幕呈现帧率已通过。
2. **Git安装未验收。** 当前代码尚未提交，不为测试安装擅自提交或推送；已验证的交付渠道是本地包与tgz。
3. 本轮不涉及游戏/模拟器/实验私服，不声称游戏美术、全地块装置运行条件或战斗行为已验证。

## 改动组织

```text
ProjectDesignSchemeDocument/
├─ UNITY_EDITOR_DESIGN.md
├─ TECHNOLOGY_STACK_BASELINE.md
└─ PlanDocumentsForImplementation/UnityEditor/UNITY_EDITOR_IMPLEMENTATION_PLAN.md
unity/
├─ Packages/io.github.lingrs-llm-researchteam.mapeditor/
│  ├─ Editor/{Core,Storage,Application,Catalog,UI,SceneTools}/
│  ├─ Tests/Editor/
│  ├─ Samples~/BasicMap/
│  └─ Documentation~/{map-editor.md,PR_DRAFT.md}
├─ verification/                 # 独立宿主安装/卸载/Player验收
├─ evidence/                     # 源摘要与验收结果
├─ prepare-dev-project.ps1
└─ run-editmode-tests.ps1
```

旧Web功能改动未撤销、未归入本次Unity新增范围。上游PR正文见包内`Documentation~/PR_DRAFT.md`，尚未发布。


## 计划完成口径

P0–P4实现、本地P5工程验收及P6审核材料已落实；不是全量游戏地块/装置机制或美术运行编辑器。Git安装验证依赖后续授权提交，PR当前只准备而未发布。旧归档保留为历史证据，使用final-v2；无提交推送、无实机操作。

## GPU呈现补验尝试与宿主清理

2026-11-03按用户要求，已正常退出本轮DevProject后台Editor；前述验收/性能宿主均已退出。重新启动的专用非batch测试完成500笔并自行清理退出，PresentMon2.6.0因Windows ETW权限不足返回6（access denied），没有生成CSV；GPU呈现仍未验收。需要管理员运行采集器或既有Performance Log Users权限，不由代理改系统组或绕过权限。其余用户浏览器、VS Code和终端保持不动。详见[gpu尝试证据](gpu-presentation-attempt-20261103.json)。测试Stopwatch时间域未证明与ETW QPC同原点，未来关联前必须验证，不能直接按字段名对齐。插件交付包未改变。

### 经用户管理员授权后的有效呈现测量

第二轮PresentMon2.6.0以管理员身份启动，固定目标PID并用Win32 QueryPerformanceCounter记录实测区间。500笔、7.4909412秒区间内1111条呈现事件，4条交换链，全部有显示时间。各交换链Present→显示p95为9.3513–13.2813ms；Present→GPU工作完成p95为1.4002–2.8492ms。两条约500次的高频交换链显示间隔p95为22.2441/22.2542ms，最大38.9082/39.0403ms；另两条低频Editor表面未混作地图帧率。

交换链尚未逐一绑定SceneView，故只签整个测试Editor的呈现数据有效，不签特定视口或点击到光子延迟。MsGPUTime包含等待，不能当GPU忙时；各链GPUBusy p95最大2.8986ms。管理员采集器和专用Unity均退出0；原开发宿主已关闭。此前权限失败记录保留为历史，此节覆盖“无GPU呈现数据”的状态。见[统计与边界](gpu-presentation-20261103.json)、[原始CSV](gpu-presentation-20261103.csv)、[时间区间及清理](gpu-presentation-harness-20261103.json)。未改插件产品代码或交付tgz。
