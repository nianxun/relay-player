# 任务计划：拆分 `MainWindow.xaml.cs`

## 目标
把 `src/Player.App/MainWindow.xaml.cs` 拆成多个职责清晰的服务/协调器/辅助类，让主窗口只保留事件转发、UI 绑定和少量窗口级状态。

## 当前阶段
阶段 12

## 各阶段

### 阶段 1：范围梳理
- [x] 理解用户意图
- [x] 确定当前主窗口里混合的职责
- [x] 记录可拆分的逻辑边界到 findings.md
- **状态：** complete

### 阶段 2：拆分方案
- [x] 确定类拆分边界和文件组织
- [x] 明确哪些逻辑留在 MainWindow
- [x] 记录拆分决策及原因
- **状态：** complete

### 阶段 3：逐步实现
- [x] 先抽出最稳定、可测试的纯逻辑
- [x] 再抽出与播放和导航相关的协调逻辑
- [x] 最后收敛主窗口事件处理
- **状态：** complete

### 阶段 4：测试与验证
- [x] 更新或新增单元测试
- [x] 执行 `dotnet test .\Player.sln`
- [x] 执行 `dotnet build .\src\Player.App\Player.App.csproj -p:OutputPath=bin\Verify\`
- **状态：** complete

### 阶段 5：收尾
- [x] 清理过时注释和重复代码
- [x] 检查主窗口是否明显变薄
- [x] 向用户汇报拆分结果和剩余风险
- **状态：** complete

### 阶段 6：取消令牌收口
- [x] 将窗口内重复的 `CancellationTokenSource` 生命周期逻辑收口为 `CancellationLease`
- [x] 修复仍然引用旧取消状态访问方式的调用点
- [x] 补充对应单元测试
- **状态：** complete

### 阶段 7：继续拆薄剩余重文件
- [x] 再次检查 `MainWindow.Episodes.cs`、`MainWindow.Servers.cs`、`MainWindow.Poster.cs` 的职责边界
- [x] 把还能独立测试的纯逻辑继续下沉到服务或辅助类
- [x] 保持窗口层只做事件转发、UI 状态和少量协调
- [x] 完成后重新构建并跑测试
- **状态：** complete

### 阶段 8：界面美化第一轮
- [x] 调整全局配色、圆角、按钮和滚动条，让界面更接近成熟桌面产品
- [x] 收紧主窗口布局，减少空白和不均匀的区域感
- [x] 顺手把登录和修改密码弹窗统一成更克制的视觉风格
- [x] 完成后重新构建并跑测试
- **状态：** complete

### 阶段 9：弹窗手感修复
- [x] 给登录和修改密码弹窗补自定义标题栏、拖拽区和关闭按钮
- [x] 统一弹窗顶部层级、间距和关闭交互
- [x] 完成后重新构建并跑测试
- **状态：** complete

### 阶段 10：主详情区精修
- [x] 收紧封面、标题、元信息和简介区域的空间占用
- [x] 调整选季、选集、播放和轨道选择的排布密度
- [x] 再做一次构建和测试验证
- **状态：** complete

### 阶段 11：GitHub 文档与自动打包
- [x] 整理源码目录位置，降低项目根目录拥挤度
- [x] 编写中英文 GitHub 项目介绍和快速启动说明
- [x] 新增 GitHub Actions 自动构建、测试、发布和打包 workflow
- [x] 本地验证 Release 构建、测试、publish 和 zip
- **状态：** complete

### 阶段 12：README 语言结构调整
- [x] 将 GitHub 默认 README 调整为英文主文档
- [x] 新增独立中文文档 `README.zh-CN.md`
- [x] 在英文文档顶部保留中文入口链接
- **状态：** complete

## 关键问题
1. 哪些逻辑应该继续留在主窗口，哪些必须下沉到服务层？
2. 是否要把导航/详情状态进一步拆成独立协调器，还是保留在主窗口里只做薄封装？
3. 需不需要补一组针对拆分后行为的测试，避免播放和详情回归？
4. `CancellationLease` 是否应该同时保留 `Token` 和 `IsCancellationRequested`，还是继续只靠 token 暴露状态？

## 已做决策
| 决策 | 理由 |
|------|------|
| 优先拆出纯逻辑和协调逻辑 | 这样风险最低，容易补测试，便于逐步验证 |
| 主窗口保留 UI 事件和少量窗口状态 | WPF 绑定和视觉状态继续留在界面层更直接 |
| 拆分过程中不改 UI 行为 | 先稳定结构，再处理观感和交互细节 |
| 使用 partial class 先完成低风险拆文件 | 这是一次结构拆分，不改变 XAML 绑定和私有成员访问，验证成本最低 |
| 第二轮下沉纯逻辑到服务类 | 媒体类型判断、播放请求构造和用户提示不依赖 WPF，适合独立测试 |
| 第三轮优先瘦服务器和选集逻辑 | `MainWindow.Servers.cs` 与 `MainWindow.Episodes.cs` 仍偏重，先把其中纯状态同步和选择逻辑下沉 |
| `CancellationLease` 暴露只读取消状态 | 能保留旧调用点的可读性，同时集中管理 `CancellationTokenSource` 生命周期 |
| 继续拆分重 partial | 通过把服务器动作和剧集上下文拆到独立文件，主窗口相关 partial 的职责边界更清晰 |
| 事件处理继续过重 | 再把浏览/播放相关事件单独拆到 `MainWindow.BrowseEvents.cs`，主窗口事件文件更聚焦于窗口生命周期和服务器入口 |
| WPF 界面文件按职责移动 | 保持原命名空间不变，只调整文件夹位置，降低 XAML 和代码隐藏迁移风险 |
| GitHub Actions 使用官方动作和 GitHub CLI | `checkout`、`setup-dotnet`、`upload-artifact` 为官方常用动作，标签发布使用 runner 自带 `gh`，避免额外第三方 release action |
| GitHub 首页优先英文 | 默认 README 面向 GitHub 访客和生态搜索，中文说明单独放到 `README.zh-CN.md`，避免双语混排影响阅读 |

## 遇到的错误
| 错误 | 尝试次数 | 解决方案 |
|------|---------|---------|
| 暂无 | 1 | 暂无 |

## 备注
- 这个任务的目标不是“重写整个窗口”，而是把职责拆开到后续可维护的粒度。
- 每完成一个拆分阶段都要更新 `progress.md`。
