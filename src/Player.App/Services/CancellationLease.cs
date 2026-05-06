namespace Player.App.Services;

/// <summary>
/// 封装一个可重复重置、可链接外部 token 的取消令牌生命周期。
/// </summary>
/// <remarks>
/// WPF 界面里经常需要“取消旧请求并启动新请求”，同时还要保留当前请求对象供引用比较。
/// 这个小封装把 Cancel/Dispose/CreateLinkedTokenSource 的重复样板集中起来，但仍然保留对象身份，方便主窗口判断最新请求是否还有效。
/// </remarks>
public sealed class CancellationLease : IDisposable
{
    private CancellationTokenSource? _source;

    /// <summary>
    /// 当前令牌；如果还没有启动请求，则返回 <see cref="CancellationToken.None"/>。
    /// </summary>
    public CancellationToken Token => _source?.Token ?? CancellationToken.None;

    /// <summary>
    /// 表示当前请求是否已经被取消；没有活动请求时按“已取消”处理，避免旧回调继续写 UI。
    /// </summary>
    public bool IsCancellationRequested => _source?.IsCancellationRequested ?? true;

    /// <summary>
    /// 取消并释放当前请求，然后启动一个新的独立令牌。
    /// </summary>
    public CancellationToken StartNew()
    {
        Cancel();
        _source = new CancellationTokenSource();
        return _source.Token;
    }

    /// <summary>
    /// 取消并释放当前请求，然后启动一个链接到外部 token 的新令牌。
    /// </summary>
    public CancellationToken StartLinked(CancellationToken parentToken)
    {
        Cancel();
        _source = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        return _source.Token;
    }

    /// <summary>
    /// 取消当前请求并释放底层源。
    /// </summary>
    public void Cancel()
    {
        if (_source is null)
        {
            return;
        }

        try
        {
            _source.Cancel();
        }
        finally
        {
            _source.Dispose();
            _source = null;
        }
    }

    /// <summary>
    /// 释放时等价于取消当前请求，便于放进 using 生命周期。
    /// </summary>
    public void Dispose()
    {
        Cancel();
    }
}
