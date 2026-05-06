using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Player.App;

/// <summary>
/// 收集 Emby 登录信息的模态窗口。
/// </summary>
public partial class LoginDialog : Window
{
    /// <summary>
    /// 使用已有服务器档案预填地址和用户名，避免用户在重新认证时重复输入。
    /// </summary>
    public LoginDialog(
        string serverUrl,
        string username,
        string title = "连接 Emby 服务器",
        string subtitle = "服务器与账户",
        string submitText = "登录",
        bool lockServerAndUsername = false)
    {
        InitializeComponent();
        TryApplyWindowIcon();
        ServerUrlTextBox.Text = serverUrl;
        UsernameTextBox.Text = username;
        Title = $"Relay Player - {title}";
        TitleTextBlock.Text = title;
        SubtitleTextBlock.Text = subtitle;
        SubmitButton.Content = submitText;
        ServerUrlTextBox.IsEnabled = !lockServerAndUsername;
        UsernameTextBox.IsEnabled = !lockServerAndUsername;
    }

    public string ServerUrl { get; private set; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    public string Password { get; private set; } = string.Empty;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerUrlTextBox.Text))
        {
            ServerUrlTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
        {
            UsernameTextBox.Focus();
            return;
        }

        PasswordBox.Focus();
    }

    /// <summary>
    /// 标题栏区域只负责拖动窗口，避免无边框弹窗失去移动能力。
    /// </summary>
    private void TitleBarDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    /// <summary>
    /// 关闭按钮直接关闭窗口，外层通过 ShowDialog 的返回值判断是否提交。
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        Submit();
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        Submit();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>
    /// 在关闭弹窗前做最小输入校验，避免主窗口收到不可用的登录参数。
    /// </summary>
    private void Submit()
    {
        var serverUrl = ServerUrlTextBox.Text.Trim();
        var username = UsernameTextBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(serverUrl) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            ValidationTextBlock.Text = "请填写服务器地址、用户名和密码。";
            return;
        }

        ServerUrl = serverUrl;
        Username = username;
        Password = password;
        DialogResult = true;
    }

    /// <summary>
    /// 登录弹窗图标只做增强显示，加载失败时不影响连接流程。
    /// </summary>
    private void TryApplyWindowIcon()
    {
        try
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/RelayPlayer.png", UriKind.Absolute));
        }
        catch
        {
        }
    }
}
