using LumaCast.Services;

namespace LumaCast.Tests;

[TestClass]
public sealed class LiveKitRoomRegistryTests
{
    [TestMethod]
    public void CreateRegistersActiveRoomWithProtectedBroadcasterKey()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var registry = new LiveKitRoomRegistry(timeProvider);

        var room = registry.Create();

        Assert.IsTrue(registry.IsActive(room.RoomName));
        Assert.IsTrue(registry.ValidateBroadcaster(room.RoomName, room.BroadcastKey));
        Assert.IsFalse(registry.ValidateBroadcaster(room.RoomName, "invalid"));
    }

    [TestMethod]
    public void EndRemovesRoomOnlyWithValidBroadcasterKey()
    {
        var registry = new LiveKitRoomRegistry(TimeProvider.System);
        var room = registry.Create();

        Assert.IsFalse(registry.End(room.RoomName, "invalid"));
        Assert.IsTrue(registry.End(room.RoomName, room.BroadcastKey));
        Assert.IsFalse(registry.IsActive(room.RoomName));
    }

    [TestMethod]
    public void IsActiveExpiresRoomAfterTwelveHours()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var registry = new LiveKitRoomRegistry(timeProvider);
        var room = registry.Create();

        timeProvider.Advance(TimeSpan.FromHours(12).Add(TimeSpan.FromSeconds(1)));

        Assert.IsFalse(registry.IsActive(room.RoomName));
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
