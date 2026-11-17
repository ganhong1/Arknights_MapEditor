# LevelData 编辑器文档

本目录记录 Unity 插件的使用、诊断、接口与文件契约。设计研究保留在仓库的 `ProjectDesignSchemeDocument/`。

| 入口 | 内容 |
|---|---|
| [项目文件结构](project-layout.md) | 源码、资源、研究、工具与生成物的职责 |
| [日志与异常运维](operations/diagnostics.md) | 查看事件、排查错误、导出诊断包 |
| [错误码](operations/error-codes.md) | 稳定错误分类和恢复操作 |
| [C# 接口约定](api/contracts.md) | 文档编辑、保存、撤销和草稿边界 |
| [文件契约](schemas/README.md) | LevelData、项目文件、诊断事件 |
| [HTTP 文档](http/README.md) | OpenAPI 与 Swagger UI 的当前范围 |
| [构建文档](building.md) | 工具版本、校验和离线站点生成 |

生成站点另含 `api/reference/` 下由实际源码和 Unity 程序集生成的 API 页面。
