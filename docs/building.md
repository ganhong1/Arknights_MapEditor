# 构建与校验文档

工具版本：DocFX 2.81.0、Swagger UI 5.34.1、JSON Schema 校验器 jsonschema 4.25.0；构建脚本使用 PowerShell 7、Python 3.10+、Node/npm 与 .NET 10 SDK。Unity 程序集由 Unity 2021.3 编译。构建不启动服务、不修改 Unity 项目或用户地图。

```powershell
python tools/docs/validate_docs.py
python -m unittest discover -s tools/docs -p 'test_*.py'
./tools/docs/build-docs.ps1 `
  -DocfxPath D:/Tools/docfx-2.81.0/docfx.exe `
  -UnityEditorRoot D:/MixTools/Unity/Editors/2021.3.39f1/Editor `
  -UnityProject D:/Projects/MapEditorAcceptance `
  -OutputRoot D:/Builds/map-editor-docs
```

`OutputRoot` 应为仓库外的新空目录。临时文件和生成物放入输出目录，工具缓存默认放在其同级 `.map-editor-doc-cache`，也可通过 `-CacheRoot` 指定 D 盘共享缓存。DocFX 工具可从[官方发布页](https://github.com/dotnet/docfx/releases/tag/v2.81.0)下载解压到 D 盘。校验器可按 `tools/docs/requirements.txt` 在已有环境安装。

先编译 Unity 项目，再运行文档构建。Core、Storage 和 Editor 从当前源码及 XML 注释生成公开 API，引用 Unity 2021.3 的模块程序集。窗口及会话契约另有说明。构建清单记录实际程序集摘要、工具版本和源码摘要，便于核对来源。

输出包含 `source/site/index.html`、`source/site/http/index.html`、`source/api/reference/` 和 `build-manifest.json`。Swagger 静态文件随站点打包；站点不依赖 CDN。HTTP 示例契约为空端点集合。

校验失败返回非零退出码。文档契约检查覆盖样例、Schema、本地 Markdown 链接及未实现 HTTP 服务边界；Unity 的完整解析和业务约束仍由 EditMode 测试负责。
