using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LumaCast.Configuration;
using LumaCast.Services;
using Microsoft.Extensions.Options;

namespace LumaCast.Tests;

[TestClass]
public sealed class LiveKitTokenServiceTests
{
    [TestMethod]
    public void CreateParticipantTokenGrantsPublishOnlyToBroadcaster()
    {
        var service = CreateService();

        var host = service.CreateParticipantToken("room-test", "host-1", "Host", canPublish: true);
        var viewer = service.CreateParticipantToken("room-test", "viewer-1", "Viewer", canPublish: false);

        Assert.AreEqual("wss://example.livekit.cloud", host.ServerUrl);
        AssertVideoGrant(host.ParticipantToken, canPublish: true, canSubscribe: false);
        AssertVideoGrant(viewer.ParticipantToken, canPublish: false, canSubscribe: true);
    }

    [TestMethod]
    public void CreateParticipantTokenThrowsWhenLiveKitIsNotConfigured()
    {
        var service = new LiveKitTokenService(Options.Create(new LiveKitOptions()));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            service.CreateParticipantToken("room-test", "viewer-1", "Viewer", canPublish: false));
    }

    private static LiveKitTokenService CreateService() => new(Options.Create(new LiveKitOptions
    {
        Url = "wss://example.livekit.cloud",
        ApiKey = "test-api-key",
        ApiSecret = "test-api-secret-with-enough-entropy"
    }));

    private static void AssertVideoGrant(string token, bool canPublish, bool canSubscribe)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        using var payload = JsonDocument.Parse(jwt.Payload.SerializeToJson());
        var video = payload.RootElement.GetProperty("video");

        Assert.AreEqual("room-test", video.GetProperty("room").GetString());
        Assert.IsTrue(video.GetProperty("roomJoin").GetBoolean());
        Assert.AreEqual(canPublish, video.GetProperty("canPublish").GetBoolean());
        Assert.AreEqual(canSubscribe, video.GetProperty("canSubscribe").GetBoolean());
        Assert.IsFalse(video.GetProperty("canPublishData").GetBoolean());
    }
}
