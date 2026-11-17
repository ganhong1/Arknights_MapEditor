# 诊断错误码

`id` 标识一次诊断事件；`operationId` 串联一次操作；`code` 是可统计的稳定分类。当前分类来自 `DiagnosticEvent.Failure`，不以异常消息文本匹配。

| code | 类别 / 级别 | 含义与处理 |
|---|---|---|
| `OPERATION_STARTED` | operation / debug | 详细日志中的操作开始 |
| `OPERATION_COMPLETED` | operation / info | 操作完成 |
| `OPERATION_RECOVERED` | recovered / info | 故障恢复 |
| `OPERATION_CANCELLED` | cancelled / info | 用户或任务取消，不按故障处理 |
| `VALIDATION_FAILED` | validation / info | JSON、格式、参数或数据校验失败；修正输入 |
| `DOCUMENT_CONFLICT` | conflict / warning | 文件摘要与预期不同；重新读取或另存 |
| `STORAGE_FAILED` | io / warning | 文件读写或权限问题；检查目录、空间、占用 |
| `OPERATION_TIMEOUT` | timeout / warning | 操作超时；检查资源规模及可用性后重试读取 |
| `INTERNAL_ERROR` | internal / error | 未归类的程序异常；保留草稿并导出诊断 |

本表是当前稳定分类。更具体的功能错误码应随实际处理代码和测试一起增加。错误本身的堆栈与内部异常类型在事件详情中；原始异常消息不直接写入诊断文件。
