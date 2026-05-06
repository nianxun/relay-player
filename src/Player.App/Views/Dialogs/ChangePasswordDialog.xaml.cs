using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Player.App;

/// <summary>
/// 收集服务器端密码修改所需的新密码和确认密码。
/// </summary>
public partial class ChangePasswordDialog : Window
{
    public ChangePasswordDialog(string username)
    {
        InitializeComponent();
        TryApplyWindowIcon();
        AccountTextBlock.Text = $"当前账户：{username}";
    }

    public string NewPassword { get; private set; } = string.Empty;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NewPasswordBox.Focus();
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

    private void ConfirmPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        Submit();
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        Submit();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>
    /// 在关闭弹窗前校验两次输入一致，避免把错误密码直接提交到 Emby 服务器。
    /// </summary>
    private void Submit()
    {
        var newPassword = NewPasswordBox.Password;
        var confirmPassword = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(confirmPassword))
        {
            ValidationTextBlock.Text = "请填写新密码并确认。";
            return;
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            ValidationTextBlock.Text = "两次输入的新密码不一致。";
            return;
        }

        NewPassword = newPassword;
        DialogResult = true;
    }

    /// <summary>
    /// 图标加载失败不影响密码修改流程。
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
