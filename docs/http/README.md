# HTTP 接口文档

当前 Unity 插件没有业务 HTTP 服务；已有能力通过 C# 和编辑器界面使用。

`openapi.json` 采用 OpenAPI 3.2.1，`paths` 为空。Swagger UI 固定 5.34.1；构建脚本将前端文件打包到本地站点，不要求用户通过 CDN 加载。页面显示当前没有可调用端点，未开启任何监听端口。

构建后打开站点的 `http/index.html` 可查看该契约。新增实际 HTTP 接口时，必须同时提交 operationId、请求/响应 Schema、错误码、示例、版本及契约测试。编辑接口需要文档版本检查，并进入工作台的编辑事务；不绕过撤销和草稿流程。

错误响应采用 RFC 9457 Problem Details，另加稳定错误码、操作编号和字段位置。服务尚未实现，当前文档不会把这些设计声明为可调用能力。

参考：[OpenAPI 3.2.1](https://spec.openapis.org/oas/v3.2.1.html)、[Swagger UI 5.34.1](https://github.com/swagger-api/swagger-ui/releases/tag/v5.34.1)、[RFC 9457](https://www.rfc-editor.org/rfc/rfc9457.html)。
