# 项目文件结构

| 位置 | 职责 | 管理方式 |
|---|---|---|
| 根目录 Web 文件、`prts-map/`、`assets/` | 既有 Web 编辑器和 PRTSMap 资源 | 保留原入口和相对引用 |
| `desktop/` | 桌面封装 | 构建产物不提交 |
| `unity/Packages/io.github.lingrs-llm-researchteam.mapeditor/` | Unity UPM 插件 | 源码与 `.meta` 一起管理 |
| 插件 `Editor/Core/` | 不可变 LevelData 和领域操作 | 无 Unity 窗口依赖 |
| 插件 `Editor/Storage/` | 文件保存、冲突检查和恢复 | 保留原子替换和摘要校验 |
| 插件 `Editor/Application/` | 文档会话、编辑历史及局部草稿 | 协调文档状态 |
| 插件 `Editor/Diagnostics/` | 日志模型、存储、队列和生命周期 | 不混入地图 JSON |
| 插件 `Editor/UI/`、`SceneTools/` | 编辑面板与原生场景交互 | Unity 编辑器线程 |
| 插件 `Editor/Catalog/` | 固定资源基线与查询 | 资源生成工具在 `tools/` |
| 插件 `Tests/Editor/` | EditMode 回归 | 独立验收项目运行 |
| `docs/` | 用户及接口文档的源文件 | 生成 API/站点不提交 |
| `tools/docs/` | 文档构建、契约检查和回归脚本 | 缓存和输出通过参数指定 |
| `ProjectDesignSchemeDocument/` | 设计、逆向依据、计划与验收记录 | 保留已有链接与历史记录 |
| `.local/Resources/` | 离线美术、音乐与音效试听包 | 按用途放置，Git 忽略 |
| `.local/UnityProjects/` | 演示、开发和验收工程 | Unity 项目缓存和自动恢复草稿随各工程管理 |
| `.local/Maps/` | 工作地图、冻结基准和演示地图 | 与插件源码独立保存 |
| `.local/History/` | 历史验证、研究及文档生成资料 | 本地归档 |

日常开发使用地图编辑器仓库当前检出的 `feature/unity-leveldata-editor`。演示工程 `.local/UnityProjects/WorkbenchDemoProject` 通过相对路径加载 `unity/Packages/` 中的插件。共享 Unity 安装和第三方包仍由本机工具链管理。

仓库中的设计与进度入口为 `ProjectDesignSchemeDocument/文档导航.md`。Unity 资源与对应的 `.meta` 一起管理，移动时保留 GUID。
