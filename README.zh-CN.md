# Relay Player

[English](./README.md)

Relay Player 是一个面向 Windows 的 Emby 桌面播放器客户端。它负责服务器登录、浏览、搜索、继续播放、媒体源选择和播放进度回传，实际视频播放交给 `mpv.net`。

项目目标很明确：用轻量桌面客户端处理 Emby 媒体库和播放状态，用成熟外部播放器处理解码、字幕、音轨和播放控制。

## 功能

- 保存多个 Emby 服务器，并从服务器列表一键切换。
- 启动时自动恢复上次可用的 Emby 登录状态。
- 浏览和搜索电影、剧集、季、集和文件夹。
- 以“继续观看”作为主要入口。
- 播放前选择媒体源、音频轨和字幕轨。
- 使用带认证的 Emby 直链启动 `mpv.net`。
- 支持从 Emby 记录的播放位置继续播放。
- 通过 mpv JSON IPC 向 Emby 回传播放开始、播放进度和停止事件。
- 开启连播时，在同一个 `mpv.net` 实例中自动播放下一集。
- 设置和日志写入 `%APPDATA%\RelayPlayer`。

## 运行要求

- Windows 10 或 Windows 11。
- 开发和构建需要 .NET 10 SDK。
- 已安装 `mpv.net`。如果不在 `PATH` 中，可在应用内选择 `mpvnet.exe`。
- 可访问的 Emby 服务器和有效用户账号。

发布包分为两种：

- `RelayPlayer-win-x64-self-contained.zip`：体积更大，但目标机器不需要额外安装 .NET 运行时。
- `RelayPlayer-win-x64-framework-dependent-portable.zip`：体积小很多，但目标机器需要安装 .NET 10 Desktop Runtime。

## 快速启动

```powershell
dotnet restore .\Player.sln
dotnet build .\Player.sln
dotnet run --project .\src\Player.App\Player.App.csproj
```

仓库中的 `global.json` 会指定 .NET 10 SDK。可以用下面命令确认本机 SDK：

```powershell
dotnet --info
```

首次运行：

1. 点击“添加服务器”。
2. 输入 Emby 地址、用户名和密码。
3. 如果没有自动检测到 `mpv.net`，手动选择 `mpvnet.exe`。
4. 从“继续观看”或搜索结果中选择媒体。
5. 选择媒体源、音频轨和字幕轨。
6. 使用 `mpv.net` 开始播放。

## 构建、测试和发布

运行测试：

```powershell
dotnet test .\Player.sln
```

生成本地 Windows x64 自包含发布目录：

```powershell
dotnet publish .\src\Player.App\Player.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o .\artifacts\RelayPlayer-win-x64
```

生成体积更小的 Windows x64 框架依赖便携目录：

```powershell
dotnet publish .\src\Player.App\Player.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=false `
  -o .\artifacts\RelayPlayer-win-x64-framework-dependent-portable
```

生成 zip 包：

```powershell
Compress-Archive -Path .\artifacts\RelayPlayer-win-x64\* -DestinationPath .\artifacts\RelayPlayer-win-x64-self-contained.zip -Force
Compress-Archive -Path .\artifacts\RelayPlayer-win-x64-framework-dependent-portable\* -DestinationPath .\artifacts\RelayPlayer-win-x64-framework-dependent-portable.zip -Force
```

## GitHub Actions 自动打包

`.github/workflows/package.yml` 会在 push、pull request、手动触发和 `v*` 标签时运行。

流程包括：

- 还原依赖
- Release 构建
- 运行测试
- 发布 `win-x64` 自包含包
- 发布 `win-x64` 框架依赖便携包
- 分别压缩 zip
- 上传 artifact
- `v*` 标签时创建或更新 GitHub Release

上传的文件名为：

- `RelayPlayer-win-x64-self-contained.zip`
- `RelayPlayer-win-x64-framework-dependent-portable.zip`

## 项目结构

```text
src/
  Player.App/
    Assets/              应用图标和图片资源
    Converters/          WPF 值转换器
    Models/              Emby、设置和浏览状态模型
    Services/            Emby API、mpv.net、IPC、播放协调和持久化服务
    Views/
      Dialogs/           登录、修改密码等弹窗
      MainWindow/        主窗口 XAML 与按职责拆分的 partial 代码
tests/
  Player.App.Tests/      单元测试
.github/workflows/      CI 和自动打包配置
```

## 数据位置

- 设置：`%APPDATA%\RelayPlayer\settings.json`
- 日志：`%APPDATA%\RelayPlayer\logs\relay-player.log`

保存内容包括服务器地址、用户名、设备 ID、Emby token 和 `mpv.net` 路径。敏感 token 会通过 Windows 当前用户范围的数据保护 API 处理。

## 已知限制

- 目前仅面向 Windows 和 `mpv.net`。
- 外部播放器需要在 URL 中携带 Emby `api_key`，因为它不能复用 WPF 客户端的 HTTP 请求头。
- 部分 Emby 服务器设置、转码策略或第三方插件可能影响直链播放能力。

## 参考

- Emby API: https://dev.emby.media/doc/restapi/
- mpv manual: https://mpv.io/manual/stable/
- mpv.net: https://github.com/mpvnet-player/mpv.net
