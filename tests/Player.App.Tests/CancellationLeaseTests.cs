using Player.App.Services;

namespace Player.App.Tests;

[TestClass]
public sealed class CancellationLeaseTests
{
    [TestMethod]
    public void StartNew_CancelsPreviousTokenAndCreatesFreshOne()
    {
        using var lease = new CancellationLease();
        var first = lease.StartNew();
        Assert.IsFalse(first.IsCancellationRequested);

        var second = lease.StartNew();

        Assert.IsTrue(first.IsCancellationRequested);
        Assert.IsFalse(second.IsCancellationRequested);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void StartLinked_ObservesParentCancellation()
    {
        using var parent = new CancellationTokenSource();
        using var lease = new CancellationLease();

        var token = lease.StartLinked(parent.Token);
        Assert.IsFalse(token.IsCancellationRequested);

        parent.Cancel();

        Assert.IsTrue(token.IsCancellationRequested);
    }

    [TestMethod]
    public void Cancel_ResetsTokenAndIsIdempotent()
    {
        var lease = new CancellationLease();
        var token = lease.StartNew();

        lease.Cancel();
        lease.Cancel();

        Assert.IsTrue(token.IsCancellationRequested);
        Assert.AreEqual(CancellationToken.None, lease.Token);
    }

    [TestMethod]
    public void IsCancellationRequested_ReflectsCurrentLeaseState()
    {
        using var lease = new CancellationLease();

        Assert.IsTrue(lease.IsCancellationRequested);

        var token = lease.StartNew();

        Assert.IsFalse(lease.IsCancellationRequested);

        lease.Cancel();

        Assert.IsTrue(lease.IsCancellationRequested);
        Assert.IsTrue(token.IsCancellationRequested);
    }
}
