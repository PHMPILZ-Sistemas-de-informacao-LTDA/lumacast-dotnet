using LumaCast.Configuration;

namespace LumaCast.Tests;

[TestClass]
public sealed class LiveKitOptionsTests
{
    [TestMethod]
    public void OptionsRequireCompleteConfigurationWithWebSocketUrl()
    {
        var empty = new LiveKitOptions();
        Assert.IsTrue(empty.IsEmpty);
        Assert.IsFalse(empty.IsConfigured);
        Assert.IsFalse(empty.HasAllValues);
        Assert.IsFalse(empty.HasValidUrl());

        var partial = new LiveKitOptions { Url = "wss://example.livekit.cloud" };
        Assert.IsFalse(partial.IsEmpty);
        Assert.IsFalse(partial.IsConfigured);
        Assert.IsFalse(partial.HasAllValues);
        Assert.IsTrue(partial.HasValidUrl());

        var invalidProtocol = new LiveKitOptions
        {
            Url = "https://example.livekit.cloud",
            ApiKey = "api-key",
            ApiSecret = "api-secret"
        };
        Assert.IsTrue(invalidProtocol.HasAllValues);
        Assert.IsFalse(invalidProtocol.IsConfigured);
        Assert.IsFalse(invalidProtocol.HasValidUrl());

        var configured = new LiveKitOptions
        {
            Url = "ws://localhost:7880",
            ApiKey = "api-key",
            ApiSecret = "api-secret"
        };
        Assert.IsFalse(configured.IsEmpty);
        Assert.IsTrue(configured.IsConfigured);
        Assert.IsTrue(configured.HasAllValues);
        Assert.IsTrue(configured.HasValidUrl());
    }
}
