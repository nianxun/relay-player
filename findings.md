# 发现与决策

## 需求
- 用户希望“直接把这个文件拆完”，指的是把 `MainWindow.xaml.cs` 拆成更小的职责单元。
- 当前项目已经存在若干服务：`PlaybackCoordinator`、`ArtworkResolver`、`EpisodeSelectionCoordinator`、`ServerProfileManager`、`SettingsStore`。
- 主窗口仍然承担了过多职责：导航、详情加载、播放请求组装、连播衔接、服务器管理、UI 状态切换、缩略图加载。

## 研究发现
- `MainWindow.xaml.cs` 仍然是整个客户端最重的文件，且包含大量 `async void` 事件处理和窗口级状态。
- 播放和下一集逻辑已经有部分外移，说明继续拆分是顺势而为，不会破坏现有架构。
- `MainWindow` 中还有一段明显可抽出的逻辑：视图导航与列表加载、详情面板状态、当前条目选择、服务器操作。
- 已采用 partial class 方式完成第一轮拆分，主文件从 2354 行降到 71 行。
- 第二轮已把媒体类型判断、播放请求构造、页面状态类型和用户提示文案从窗口下沉到模型/服务层。
- 第三轮已把服务器档案应用、会话失效、删除 fallback 选择下沉到 `ServerProfileManager`，并把部分选集纯逻辑下沉到 `EpisodeSelectionCoordinator`。
- 当前正在收口 `CancellationLease`，目标是统一管理取消源生命周期，同时保留 `Token` 和只读取消状态，减少窗口里散落的 `CancellationTokenSource` 样板。
- 本轮又把服务器动作入口抽到 `MainWindow.ServerActions.cs`，把继续观看反向加载季/集上下文抽到 `MainWindow.EpisodeContext.cs`，`MainWindow.Servers.cs` 和 `MainWindow.Episodes.cs` 都明显变薄。
- 又把列表、季/集、媒体源和播放按钮相关事件抽到 `MainWindow.BrowseEvents.cs`，让 `MainWindow.Events.cs` 更偏窗口生命周期和服务器菜单入口。
- `MainWindow.RuntimeUi.cs` 继续拆出 `MainWindow.PlayedState.cs` 和 `MainWindow.ViewShell.cs`，现在运行时文件只保留取消和后台状态通知。
- `MainWindow.Navigation.cs` 继续拆出 `MainWindow.ViewLoading.cs`，现在导航文件只保留搜索、目录打开、详情进入和返回栈入口。
- 界面第一轮美化已经把主色板、按钮、滚动条、右键菜单和工具提示统一到同一套深色视觉里，并把登录/改密弹窗改成更像独立面板的样式。
- 本轮又给登录和修改密码弹窗补了自己的标题栏、拖拽区和关闭按钮，避免无边框窗口只剩一块不能移动的面板。
- 主详情区这一轮进一步压缩了封面、标题、简介和控制区的空间，控件宽度和间距更收敛，避免右侧面板看起来过空。
- 截图反馈显示选季控件被压到 96px 后，选中项只能显示“第 2...”；季标签本身是短文本，问题来自控件宽度和下拉按钮占位。
- GitHub 文档与自动打包阶段确认了当前 GitHub Actions 可使用 `actions/checkout@v6`、`actions/setup-dotnet@v4`、`actions/upload-artifact@v4` 完成 Windows runner 上的 .NET 构建、测试和 artifact 上传。
- .NET 发布阶段确认当前发布命令可使用 `win-x64`、`--self-contained true`、`PublishSingleFile`、`IncludeNativeLibrariesForSelfExtract` 和 `EnableCompressionInSingleFile` 生成 zip 友好的发布目录。
- 源码目录整理后，主窗口 partial 文件集中到 `Views/MainWindow`，登录与修改密码弹窗集中到 `Views/Dialogs`，转换器集中到 `Converters`；命名空间保持 `Player.App`，降低 XAML 类名和现有引用的迁移风险。

## 技术决策
| 决策 | 理由 |
|------|------|
| 先抽纯逻辑，再抽协调器 | 纯逻辑最容易测试，也最不依赖 WPF 视觉树 |
| 保留主窗口事件入口 | WPF 事件和绑定在窗口层更自然，避免过度 MVVM 化 |
| 逐步迁移而不是一次性大改 | 能持续构建和验证，减少界面回归 |
| 第一轮使用 partial 文件拆分 | 不改行为，保留 XAML 事件绑定和私有字段访问，适合作为大文件拆分的安全第一步 |
| 下沉 `PlaybackRequestFactory` | 播放请求是纯输入到输出，适合通过单元测试覆盖续播、字幕关闭和轨道选择 |
| 下沉 `EmbyItemKind` | 类型判断散落在窗口和选集逻辑中，集中后减少字符串比较重复 |
| 下沉 `UserFacingMessages` | 错误提示和加载提示集中后更容易保持一致，也能测试关键错误文案 |
| 扩展 `ServerProfileManager` | 服务器设置同步属于档案业务逻辑，不应长期留在 WPF 窗口里 |
| 扩展 `EpisodeSelectionCoordinator` | 初始季选择、季请求 ID、按 ID 找集都是纯选集逻辑，适合测试覆盖 |
| README 改为中英双语 | GitHub 首页需要同时服务中文使用者和潜在英文读者，快速启动、发布和数据位置都应在首页可见 |
| 自动打包只产出 Windows x64 | 当前应用是 WPF + mpv.net 客户端，跨平台包没有意义，先保证主要目标平台稳定 |

## 遇到的问题
| 问题 | 解决方案 |
|------|---------|
| `MainWindow.xaml.cs` 过大，职责交叠 | 用规划文件先划出边界，再按边界拆类 |
| 一次性抽成服务风险较高 | 先按职责拆成 partial 文件，后续可继续把纯逻辑从 partial 文件下沉到服务 |
| 直接替换方法名时会影响方法声明 | 已删除窗口中被替换坏的重复方法声明，改为调用服务类 |
| `_activeSeasonId` 只写不读 | 已删除字段和写入点，避免保留误导性状态 |
| `CancellationTokenSource` 生命周期重复 | 抽出 `CancellationLease`，并在它上面补只读取消状态，方便旧调用点平滑迁移 |
| 服务器操作继续过重 | 抽出 `MainWindow.ServerActions.cs`，把登录、切换、修改密码和删除从状态辅助方法里分离 |
| 剧集上下文加载继续过重 | 抽出 `MainWindow.EpisodeContext.cs`，把后台补全季/集选择器的逻辑单独放开 |
| 浏览/播放事件继续过重 | 抽出 `MainWindow.BrowseEvents.cs`，减少统一事件文件里的交互密度 |
| 运行时 UI 辅助继续混杂 | 抽出 `MainWindow.PlayedState.cs` 和 `MainWindow.ViewShell.cs`，分别承载已播放状态同步和主视图外壳控制 |
| 导航入口和列表加载混在一起 | 抽出 `MainWindow.ViewLoading.cs`，让导航入口和 Emby 列表加载流程分离 |
| 界面观感偏硬 | 用更统一的深色调、圆角和弹出层样式把应用做得更像成熟桌面客户端 |
| 无边框弹窗缺少手感 | 给登录/改密弹窗加自定义标题栏和关闭按钮，保留移动与关闭能力 |
| 主详情区显得松散 | 收紧封面尺寸、标题层级和选集/播放控件宽度，让右侧内容更集中 |
| 选季显示被截断 | 将季选择器从 96px 放宽到 126px，并略微放宽集选择器，保证“第 N 季”完整展示 |
| 项目根目录源码文件过散 | 将窗口、弹窗、转换器按 UI 职责归档到子目录，只更新 `StartupUri` |
| GitHub 发布包需要可重复生成 | 新增 `.github/workflows/package.yml`，并用本地 publish + zip 验证参数 |

## 资源
- [src/Player.App/Views/MainWindow/MainWindow.xaml.cs](./src/Player.App/Views/MainWindow/MainWindow.xaml.cs)
- [.github/workflows/package.yml](./.github/workflows/package.yml)
- [README.md](./README.md)
- [src/Player.App/Services/PlaybackCoordinator.cs](./src/Player.App/Services/PlaybackCoordinator.cs)
- [src/Player.App/Services/ArtworkResolver.cs](./src/Player.App/Services/ArtworkResolver.cs)
- [src/Player.App/Services/EpisodeSelectionCoordinator.cs](./src/Player.App/Services/EpisodeSelectionCoordinator.cs)
- [src/Player.App/Services/ServerProfileManager.cs](./src/Player.App/Services/ServerProfileManager.cs)
- [src/Player.App/Views/MainWindow/MainWindow.Events.cs](./src/Player.App/Views/MainWindow/MainWindow.Events.cs)
- [src/Player.App/Views/MainWindow/MainWindow.Navigation.cs](./src/Player.App/Views/MainWindow/MainWindow.Navigation.cs)
- [src/Player.App/Views/MainWindow/MainWindow.Detail.cs](./src/Player.App/Views/MainWindow/MainWindow.Detail.cs)
- [src/Player.App/Views/MainWindow/MainWindow.Playback.cs](./src/Player.App/Views/MainWindow/MainWindow.Playback.cs)
- [src/Player.App/Views/MainWindow/MainWindow.AutoPlayback.cs](./src/Player.App/Views/MainWindow/MainWindow.AutoPlayback.cs)
- [src/Player.App/Views/MainWindow/MainWindow.Servers.cs](./src/Player.App/Views/MainWindow/MainWindow.Servers.cs)
- [src/Player.App/Views/MainWindow/MainWindow.ServerActions.cs](./src/Player.App/Views/MainWindow/MainWindow.ServerActions.cs)
- [src/Player.App/Views/MainWindow/MainWindow.ServerEvents.cs](./src/Player.App/Views/MainWindow/MainWindow.ServerEvents.cs)
- [src/Player.App/Views/MainWindow/MainWindow.ServerMutations.cs](./src/Player.App/Views/MainWindow/MainWindow.ServerMutations.cs)
- [src/Player.App/Views/MainWindow/MainWindow.ViewLoading.cs](./src/Player.App/Views/MainWindow/MainWindow.ViewLoading.cs)
- [src/Player.App/Views/MainWindow/MainWindow.ViewShell.cs](./src/Player.App/Views/MainWindow/MainWindow.ViewShell.cs)
- [src/Player.App/Views/MainWindow/MainWindow.PlayedState.cs](./src/Player.App/Views/MainWindow/MainWindow.PlayedState.cs)
- [src/Player.App/Models/BrowseState.cs](./src/Player.App/Models/BrowseState.cs)
- [src/Player.App/Services/EmbyItemKind.cs](./src/Player.App/Services/EmbyItemKind.cs)
- [src/Player.App/Services/PlaybackRequestFactory.cs](./src/Player.App/Services/PlaybackRequestFactory.cs)
- [src/Player.App/Services/UserFacingMessages.cs](./src/Player.App/Services/UserFacingMessages.cs)

## 视觉/浏览器发现
- 无
