using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Idvbp.Neo.Models;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Idvbp.Neo.Server.Services;

public interface ICurrentRoomStateService
{
    Task<CurrentRoomPayload> GetCurrentRoomAsync(CancellationToken cancellationToken = default);

    Task<CurrentRoomPayload> SetCurrentRoomAsync(string? roomId, CancellationToken cancellationToken = default);
}

public sealed class CurrentRoomStateService : ICurrentRoomStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHubContext<GameHub> _hubContext;
    private readonly IRoomService _roomService;
    private readonly object _syncRoot = new();
    private string? _currentRoomId;

    public CurrentRoomStateService(IHubContext<GameHub> hubContext, IRoomService roomService)
    {
        _hubContext = hubContext;
        _roomService = roomService;
    }

    public async Task<CurrentRoomPayload> GetCurrentRoomAsync(CancellationToken cancellationToken = default)
    {
        var roomId = Volatile.Read(ref _currentRoomId);
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return new CurrentRoomPayload();
        }

        var room = await _roomService.GetRoomAsync(roomId, cancellationToken);
        if (room is null)
        {
            lock (_syncRoot)
            {
                if (string.Equals(_currentRoomId, roomId, StringComparison.OrdinalIgnoreCase))
                {
                    _currentRoomId = null;
                }
            }

            return new CurrentRoomPayload();
        }

        return CreatePayload(room);
    }

    public async Task<CurrentRoomPayload> SetCurrentRoomAsync(string? roomId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            lock (_syncRoot)
            {
                _currentRoomId = null;
            }

            var emptyPayload = new CurrentRoomPayload();
            await BroadcastAsync(emptyPayload, cancellationToken);
            return emptyPayload;
        }

        var room = await _roomService.GetRoomAsync(roomId.Trim(), cancellationToken);
        if (room is null)
        {
            throw new KeyNotFoundException($"Room '{roomId}' was not found.");
        }

        lock (_syncRoot)
        {
            _currentRoomId = room.RoomId;
        }

        var payload = CreatePayload(room);
        await BroadcastAsync(payload, cancellationToken);
        return payload;
    }

    private Task BroadcastAsync(CurrentRoomPayload payload, CancellationToken cancellationToken)
        => _hubContext.Clients.All.SendAsync(GameHub.CurrentRoomChangedMethodName, payload, cancellationToken);

    private static CurrentRoomPayload CreatePayload(BpRoom room)
        => new()
        {
            RoomId = room.RoomId,
            RoomName = room.RoomName,
            CurrentRound = room.CurrentRound,
            CurrentPhase = room.CurrentPhase.ToString(),
            Room = JsonSerializer.SerializeToElement(RoomService.StripLogoData(room), JsonOptions),
            OccurredAtUtc = DateTimeOffset.UtcNow
        };
}
