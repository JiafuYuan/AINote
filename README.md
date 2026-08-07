# AI 记事本

一个模仿滴答清单组织方式的 AI 记事本，使用 Avalonia 12 + Semi.Avalonia 构建，支持 Windows 桌面和 Android 全平台使用。

## 功能

- 快速记录：顶部输入内容，按 Enter 或点击“添加”保存
- AI 自动分析：自动识别分类、标签、1-5 星评级和一句话摘要
- 本地兜底：未配置 AI 时也会按关键词规则自动归类
- 智能清单：全部、今天、即将到来、星级、未分类
- 分类与标签：自动归类、按分类和标签筛选
- 详情编辑：标题、内容、摘要、分类、标签、星级、日期
- 搜索：按标题、内容、摘要、分类、标签搜索
- 响应式布局：桌面三栏，窄屏自动切换为单页式导航
- 本地存储：数据保存在 JSON 文件，不依赖服务器

## 运行

```powershell
dotnet run --project src/AINote/AINote.csproj -f net10.0
```

## 构建

桌面：

```powershell
dotnet build src/AINote/AINote.csproj -c Release -f net10.0
```

Android：

```powershell
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
$env:ANDROID_SDK_ROOT = "$env:LOCALAPPDATA\Android\Sdk"
dotnet build src/AINote/AINote.csproj -c Release -f net10.0-android -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" -p:AndroidPackageFormats=apk
```

## AI 配置

点击右上角“AI 设置”：

- 启用 AI 分析
- API Base URL：OpenAI 兼容接口地址
- API Key
- 模型：默认 `gpt-4o-mini`
- 温度：默认 `0.2`

设置保存在 `%APPDATA%\AINote\settings.json`，笔记保存在 `%APPDATA%\AINote\notes.json`；Android 下保存到应用私有数据目录。
