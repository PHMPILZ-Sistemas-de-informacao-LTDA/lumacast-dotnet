using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace LumaCast.Services;

/// <summary>
/// Mantém em memória as salas LiveKit ativas e suas chaves de apresentador.
/// Adequado para uma única instância; produção distribuída deve usar armazenamento compartilhado.
/// </summary>
public sealed class LiveKitRoomRegistry
{
    private readonly ConcurrentDictionary<string, RoomRegistration> _rooms = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>Inicializa o registro com uma fonte de tempo substituível em testes.</summary>
    /// <param name="timeProvider">Fonte usada para criação e expiração das salas.</param>
    public LiveKitRoomRegistry(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>Cria uma sala com nome e chave criptograficamente aleatórios.</summary>
    /// <returns>O registro que deve ser entregue somente ao apresentador.</returns>
    public RoomRegistration Create()
    {
        RemoveExpiredRooms();
        var roomName = $"lumacast-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant()}";
        var broadcastKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var registration = new RoomRegistration(roomName, broadcastKey, _timeProvider.GetUtcNow());
        _rooms[roomName] = registration;
        return registration;
    }

    /// <summary>Verifica se a sala existe e ainda está dentro do período de 12 horas.</summary>
    /// <param name="roomName">Nome da sala.</param>
    /// <returns><see langword="true"/> quando a sala pode receber participantes.</returns>
    public bool IsActive(string roomName)
    {
        return _rooms.TryGetValue(roomName, out var room) &&
               room.CreatedAt > _timeProvider.GetUtcNow().AddHours(-12);
    }

    /// <summary>Compara em tempo constante a chave fornecida com a chave da sala.</summary>
    /// <param name="roomName">Nome da sala.</param>
    /// <param name="broadcastKey">Chave apresentada pelo transmissor.</param>
    /// <returns><see langword="true"/> quando a chave é autêntica.</returns>
    public bool ValidateBroadcaster(string roomName, string? broadcastKey)
    {
        if (broadcastKey is null || !_rooms.TryGetValue(roomName, out var room)) return false;
        return FixedTimeEquals(room.BroadcastKey, broadcastKey);
    }

    /// <summary>Encerra uma sala quando a chave do apresentador é válida.</summary>
    /// <param name="roomName">Nome da sala.</param>
    /// <param name="broadcastKey">Chave apresentada pelo transmissor.</param>
    /// <returns><see langword="true"/> quando a sala foi removida.</returns>
    public bool End(string roomName, string? broadcastKey)
    {
        if (!ValidateBroadcaster(roomName, broadcastKey)) return false;
        return _rooms.TryRemove(roomName, out _);
    }

    private void RemoveExpiredRooms()
    {
        var cutoff = _timeProvider.GetUtcNow().AddHours(-12);
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

/// <summary>Representa uma sala temporária registrada pelo backend.</summary>
/// <param name="RoomName">Nome aleatório da sala LiveKit.</param>
/// <param name="BroadcastKey">Chave privada usada para autenticar o apresentador.</param>
/// <param name="CreatedAt">Instante UTC em que o registro foi criado.</param>
public sealed record RoomRegistration(string RoomName, string BroadcastKey, DateTimeOffset CreatedAt);
