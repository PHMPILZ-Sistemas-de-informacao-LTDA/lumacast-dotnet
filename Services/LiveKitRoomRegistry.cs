using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace LumaCast.Services;

public sealed class LiveKitRoomRegistry
{
    private readonly ConcurrentDictionary<string, RoomRegistration> _rooms = new();

    public RoomRegistration Create()
    {
        RemoveExpiredRooms();
        var roomName = $"lumacast-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant()}";
        var broadcastKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var registration = new RoomRegistration(roomName, broadcastKey, DateTimeOffset.UtcNow);
        _rooms[roomName] = registration;
        return registration;
    }

    public bool IsActive(string roomName)
    {
        return _rooms.TryGetValue(roomName, out var room) &&
               room.CreatedAt > DateTimeOffset.UtcNow.AddHours(-12);
    }

    public bool ValidateBroadcaster(string roomName, string? broadcastKey)
    {
        if (broadcastKey is null || !_rooms.TryGetValue(roomName, out var room)) return false;
        return FixedTimeEquals(room.BroadcastKey, broadcastKey);
    }

    public bool End(string roomName, string? broadcastKey)
    {
        if (!ValidateBroadcaster(roomName, broadcastKey)) return false;
        return _rooms.TryRemove(roomName, out _);
    }

    private void RemoveExpiredRooms()
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-12);
        foreach (var room in _rooms.Where(entry => entry.Value.CreatedAt < cutoff))
        {
            _rooms.TryRemove(room.Key, out _);
        }
    }

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        var expectedBytes = Convert.FromHexString(expected);
        byte[] suppliedBytes;
        try
        {
            suppliedBytes = Convert.FromHexString(supplied);
        }
        catch (FormatException)
        {
            return false;
        }

        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}

public sealed record RoomRegistration(string RoomName, string BroadcastKey, DateTimeOffset CreatedAt);
