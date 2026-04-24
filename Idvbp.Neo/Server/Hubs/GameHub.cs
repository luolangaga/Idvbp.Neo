using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace Idvbp.Neo.Server.Hubs;

public class GameHub : Hub
{
    public const string RoomEventMethodName = "RoomEvent";
    private static int _connectedClients;
    private readonly RoomSubscriptionRegistry _subscriptionRegistry;
    private readonly IRoomService _roomService;

    public GameHub(RoomSubscriptionRegistry subscriptionRegistry, IRoomService roomService)
    {
        _subscriptionRegistry = subscriptionRegistry;
        _roomService = roomService;
    }

    public static string BuildRoomGroupName(string roomId) => $"room:{roomId}";

    public static string BuildEventGroupName(string roomId, string eventType) => $"{BuildRoomGroupName(roomId)}:event:{eventType}";

    public override async Task OnConnectedAsync()
    {
        Interlocked.Increment(ref _connectedClients);
        await Clients.All.SendAsync("ClientCountChanged", _connectedClients);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Interlocked.Decrement(ref _connectedClients);
        var rooms = _subscriptionRegistry.RemoveConnection(Context.ConnectionId);
        foreach (var room in rooms)
        {
            foreach (var eventType in room.Value)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildEventGroupName(room.Key, eventType));
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildRoomGroupName(room.Key));
        }

        await Clients.All.SendAsync("ClientCountChanged", _connectedClients);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinRoom(string roomName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, BuildRoomGroupName(roomName));
        await Clients.Group(BuildRoomGroupName(roomName)).SendAsync("UserJoined", Context.ConnectionId, roomName);
    }

    public async Task LeaveRoom(string roomName)
    {
        var eventTypes = _subscriptionRegistry.RemoveRoom(Context.ConnectionId, roomName);
        foreach (var eventType in eventTypes)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildEventGroupName(roomName, eventType));
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildRoomGroupName(roomName));
        await Clients.Group(BuildRoomGroupName(roomName)).SendAsync("UserLeft", Context.ConnectionId, roomName);
    }

    public async Task BroadcastToRoom(string roomName, string eventName, object data)
    {
        await Clients.Group(BuildRoomGroupName(roomName)).SendAsync(eventName, data);
    }

    public async Task BroadcastAll(string eventName, object data)
    {
        await Clients.All.SendAsync(eventName, data);
    }

    public async Task<IReadOnlyCollection<string>> ReplaceSubscriptions(string roomId, IEnumerable<string> eventTypes)
    {
        var normalized = NormalizeEventTypes(eventTypes);
        var previous = _subscriptionRegistry.Get(Context.ConnectionId, roomId);

        foreach (var eventType in previous.Except(normalized, StringComparer.OrdinalIgnoreCase))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, BuildEventGroupName(roomId, eventType));
        }

        foreach (var eventType in normalized.Except(previous, StringComparer.OrdinalIgnoreCase))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, BuildEventGroupName(roomId, eventType));
        }

        return _subscriptionRegistry.Replace(Context.ConnectionId, roomId, normalized);
    }

    public Task<IReadOnlyCollection<string>> SubscribeToEvents(string roomId, IEnumerable<string> eventTypes)
    {
        var merged = _subscriptionRegistry.Get(Context.ConnectionId, roomId)
            .Concat(eventTypes ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return ReplaceSubscriptions(roomId, merged);
    }

    public Task<IReadOnlyCollection<string>> UnsubscribeFromEvents(string roomId, IEnumerable<string> eventTypes)
    {
        var excluded = NormalizeEventTypes(eventTypes);
        var filtered = _subscriptionRegistry.Get(Context.ConnectionId, roomId)
            .Where(x => !excluded.Contains(x, StringComparer.OrdinalIgnoreCase));
        return ReplaceSubscriptions(roomId, filtered);
    }

    public Task<IReadOnlyCollection<string>> GetAvailableEventTypes()
        => Task.FromResult((IReadOnlyCollection<string>)RoomEventNames.All);

    public async Task RequestRoomSnapshot(string roomId)
    {
        var room = await _roomService.GetRoomAsync(roomId);
        if (room is null)
        {
            throw new HubException($"Room '{roomId}' was not found.");
        }

        await Clients.Caller.SendAsync(RoomEventMethodName, new RoomEventEnvelope
        {
            RoomId = roomId,
            EventType = RoomEventNames.RoomSnapshot,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(room)
        });
    }

    private static string[] NormalizeEventTypes(IEnumerable<string> eventTypes)
        => (eventTypes ?? [])
            .Where(x => RoomEventNames.All.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
