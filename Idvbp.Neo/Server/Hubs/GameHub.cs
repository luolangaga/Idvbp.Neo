using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace Idvbp.Neo.Server.Hubs;

/// <summary>
/// SignalR 游戏中心，处理客户端连接、房间订阅与实时事件分发。
/// </summary>
public class GameHub : Hub
{
    /// <summary>
    /// 房间事件方法名称常量。
    /// </summary>
    public const string RoomEventMethodName = "RoomEvent";
    public const string CurrentRoomChangedMethodName = "CurrentRoomChanged";

    private static int _connectedClients;
    private readonly RoomSubscriptionRegistry _subscriptionRegistry;
    private readonly IRoomService _roomService;
    private readonly ICurrentRoomStateService _currentRoomStateService;

    /// <summary>
    /// 初始化游戏中心。
    /// </summary>
    /// <param name="subscriptionRegistry">房间订阅注册表。</param>
    /// <param name="roomService">房间服务。</param>
    public GameHub(
        RoomSubscriptionRegistry subscriptionRegistry,
        IRoomService roomService,
        ICurrentRoomStateService currentRoomStateService)
    {
        _subscriptionRegistry = subscriptionRegistry;
        _roomService = roomService;
        _currentRoomStateService = currentRoomStateService;
    }

    /// <summary>
    /// 构建房间组名称。
    /// </summary>
    /// <param name="roomId">房间标识。</param>
    /// <returns>房间组名称。</returns>
    public static string BuildRoomGroupName(string roomId) => $"room:{roomId}";

    /// <summary>
    /// 构建事件组名称。
    /// </summary>
    /// <param name="roomId">房间标识。</param>
    /// <param name="eventType">事件类型。</param>
    /// <returns>事件组名称。</returns>
    public static string BuildEventGroupName(string roomId, string eventType) => $"{BuildRoomGroupName(roomId)}:event:{eventType}";

    /// <summary>
    /// 客户端连接时增加连接计数并通知所有客户端。
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        Interlocked.Increment(ref _connectedClients);
        await Clients.All.SendAsync("ClientCountChanged", _connectedClients);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// 客户端断开连接时清理订阅并减少连接计数。
    /// </summary>
    /// <param name="exception">断开连接异常（若有）。</param>
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

    /// <summary>
    /// 将当前连接加入指定房间组。
    /// </summary>
    /// <param name="roomName">房间名称。</param>
    public async Task JoinRoom(string roomName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, BuildRoomGroupName(roomName));
        await Clients.Group(BuildRoomGroupName(roomName)).SendAsync("UserJoined", Context.ConnectionId, roomName);
    }

    /// <summary>
    /// 将当前连接从指定房间组移除，并清理相关订阅。
    /// </summary>
    /// <param name="roomName">房间名称。</param>
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

    /// <summary>
    /// 向指定房间广播事件。
    /// </summary>
    /// <param name="roomName">房间名称。</param>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据。</param>
    public async Task BroadcastToRoom(string roomName, string eventName, object data)
    {
        await Clients.Group(BuildRoomGroupName(roomName)).SendAsync(eventName, data);
    }

    /// <summary>
    /// 向所有连接客户端广播事件。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据。</param>
    public async Task BroadcastAll(string eventName, object data)
    {
        await Clients.All.SendAsync(eventName, data);
    }

    /// <summary>
    /// 替换当前连接在指定房间的事件订阅。
    /// </summary>
    /// <param name="roomId">房间标识。</param>
    /// <param name="eventTypes">新的事件类型集合。</param>
    /// <returns>规范化后的事件类型集合。</returns>
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

    /// <summary>
    /// 订阅指定房间的事件。
    /// </summary>
    /// <param name="roomId">房间标识。</param>
    /// <param name="eventTypes">要订阅的事件类型集合。</param>
    /// <returns>当前订阅的事件类型集合。</returns>
    public Task<IReadOnlyCollection<string>> SubscribeToEvents(string roomId, IEnumerable<string> eventTypes)
    {
        var merged = _subscriptionRegistry.Get(Context.ConnectionId, roomId)
            .Concat(eventTypes ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return ReplaceSubscriptions(roomId, merged);
    }

    /// <summary>
    /// 取消订阅指定房间的事件。
    /// </summary>
    /// <param name="roomId">房间标识。</param>
    /// <param name="eventTypes">要取消订阅的事件类型集合。</param>
    /// <returns>当前订阅的事件类型集合。</returns>
    public Task<IReadOnlyCollection<string>> UnsubscribeFromEvents(string roomId, IEnumerable<string> eventTypes)
    {
        var excluded = NormalizeEventTypes(eventTypes);
        var filtered = _subscriptionRegistry.Get(Context.ConnectionId, roomId)
            .Where(x => !excluded.Contains(x, StringComparer.OrdinalIgnoreCase));
        return ReplaceSubscriptions(roomId, filtered);
    }

    /// <summary>
    /// 获取可用的事件类型列表。
    /// </summary>
    /// <returns>所有支持的事件类型。</returns>
    public Task<IReadOnlyCollection<string>> GetAvailableEventTypes()
        => Task.FromResult((IReadOnlyCollection<string>)RoomEventNames.All);

    /// <summary>
    /// 请求指定房间的完整快照。
    /// </summary>
    /// <param name="roomId">房间标识。</param>
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

    public Task<CurrentRoomPayload> RequestCurrentRoom()
        => _currentRoomStateService.GetCurrentRoomAsync();

    public Task<CurrentRoomPayload> SetCurrentRoom(string? roomId)
        => _currentRoomStateService.SetCurrentRoomAsync(roomId);

    /// <summary>
    /// 规范化事件类型集合，过滤无效类型并去重。
    /// </summary>
    /// <param name="eventTypes">原始事件类型集合。</param>
    /// <returns>规范化后的事件类型数组。</returns>
    private static string[] NormalizeEventTypes(IEnumerable<string> eventTypes)
        => (eventTypes ?? [])
            .Where(x => RoomEventNames.All.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
