# 文件契约

采用 JSON Schema 2020-12。Schema 描述已核实的结构约束，代码负责跨字段引用和磁盘边界检查。

| Schema | 内容 | 仍由代码核验 |
|---|---|---|
| `leveldata.schema.json` | 编辑器载入所需的 mapData、map、tiles | 矩形尺寸一致、索引小于 tiles 长度、引用地块必须为对象、16 MiB/深度限制 |
| `arkmap.schema.json` | version、levelDataPath、artMappings | 相对路径段、Windows 保留名、重解析点、重复映射 key |
| `diagnostic-event.schema.json` | 结构化日志事件 | 操作关联、写入与保留策略 |

LevelData 的未知成员、未引用历史 tiles 值按现有解析器保留。本 Schema 是编辑器载入契约，不是全部游戏机制的值域枚举。原版字段的具体消费关系见设计文档。

`examples/` 保存可独立分发的小样例。`tools/docs/validate_docs.py` 检查 Schema 自身、样例、额外语义约束、Markdown 本地链接和 HTTP 文档边界。

诊断事件 Schema 对应 `schemaVersion: 1`。升级前的日志没有该字段，历史日志读取器保留兼容；导出中可能同时包含旧记录，应按版本分别处理，不把旧格式当成新版事件。
