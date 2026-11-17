# C# 接口约定

生成参考页呈现当前源码的公开类型和方法。公开 C# 成员不等于远程 HTTP 接口。

| 接口 | 输入 / 输出 | 状态、异常与副作用 |
|---|---|---|
| `LevelDocument.Parse(json)` | JSON → 不可变文档 | 严格 JSON、16 MiB、深度 64、地图宽高 1–256；错误携带字段位置；不写磁盘 |
| `LevelDocument.Create(width,height)` | 尺寸 → 新文档 | 创建独立对象，不覆盖已打开地图 |
| `LevelDocument.WithSection(name,json)` | 根字段与 JSON → 新文档 | 返回新对象，保留其他字段；调用者负责提交与撤销 |
| `DocumentSession.Edit(next,label)` | 新文档 → 会话修改 | Unity 编辑器线程；建立撤销记录，不能替代工作台的局部草稿重绑定流程 |
| `DocumentSession.Save(path)` | 文件路径 → 保存状态 | 成功后更新摘要；失败不标记已保存；遵守原文件冲突检查 |
| `DocumentFileStore.Read(path)` | 路径 → 文本与 SHA-256 | 读取有大小限制；失败抛文件/编码/数据异常 |
| `DocumentFileStore.Save(path,text,expectedHash)` | 文本与预期摘要 → 保存快照 | `null` 摘要仅创建；存在文件使用原子替换；不是跨进程 CAS |
| `ArkMapProjectStore.Read/Save` | `.arkmap` ↔ 项目对象 | 管理相对资源路径，不把美术映射写入 LevelData |
| `RecoverySnapshot` | 正文、草稿、已保存版本及来源摘要 | 局部草稿可为未完成 JSON；恢复前校验完整封套 |

## 编辑事务

工作台先完成字段/引用校验及草稿重绑定准备，再提交文档。分支、路线、波次等 UI 应复用工作台的提交入口；直接调用底层文档方法不会自动更新窗口的局部草稿。

撤销改变编辑版本，不撤销已经成功写入磁盘的保存状态。字段缺失、JSON null、空集合保持区别；历史未知字段不能在编辑无关字段时被删除。

## 错误与线程

数据校验使用 `LevelDocumentException`、`FormatException` 等具体异常；文件版本冲突使用 `DocumentFileConflictException`。UI 边界展示用户操作所需信息并关联诊断编号。异常不得使未应用草稿被清空。

Core/Storage 不调用窗口 API；Unity 会话、资源加载和窗口操作在编辑器线程执行。日志后台线程只处理已捕获的普通数据与文件 IO。

## 版本

资源研究基线为 PC 2.7.81；插件版本来自 UPM `package.json`。生成 API 文档会记录输入程序集的 SHA-256；对外发布前应先编译当前源码，避免混用旧 DLL。
