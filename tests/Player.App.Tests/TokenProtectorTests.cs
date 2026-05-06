using Player.App.Services;

namespace Player.App.Tests;

[TestClass]
public sealed class TokenProtectorTests
{
    [TestMethod]
    public void Protect_ThenUnprotect_RestoresOriginalToken()
    {
        var protector = new TokenProtector();
        var token = "sample-token-for-relay-player";

        var protectedToken = protector.Protect(token);
        var restoredToken = protector.Unprotect(protectedToken);

        Assert.AreNotEqual(token, protectedToken);
        Assert.AreEqual(token, restoredToken);
    }

    [TestMethod]
    public void Unprotect_LegacyPlainToken_ReturnsPlainToken()
    {
        var protector = new TokenProtector();
        var legacyToken = "legacy-plain-token";

        var restoredToken = protector.Unprotect(legacyToken);

        Assert.AreEqual(legacyToken, restoredToken);
    }
}
