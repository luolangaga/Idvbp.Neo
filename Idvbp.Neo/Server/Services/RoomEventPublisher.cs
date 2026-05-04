using System;
using System.Text.Json;
using System.Threading.Tasks;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Idvbp.Neo.Server.Services;

/// <summary>
/// 房间事件发布器接口，定义房间事件的发布契约。
/// </summary>
public interface IRoomEventPublisher
{
    /// <summary>
    /// 异步发布房间事件。
    /// </summary>
    /// <param name="roomId">房间标识。</param>
    /// <param name="eventType">事件类型。</param>
    /// <param name="payload">事件载荷数据。</param>
    Task PublishAsync(string roomId, string eventType, object payload);
}

/// <summary>
/// 基于 SignalR 的房间事件发布器实现，将事件发送到订阅了对应事件类型的客户端组。
/// </summary>
public sealed class RoomEventPublisher : IRoomEventPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHubContext<GameHub> _hubContext;

    /// <summary>
    /// 初始化房间事件发布器。
    /// </summary>
    /// <param name="hubContext">SignalR 游戏中心上下文。</param>
    public RoomEventPublisher(IHubContext<GameHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <summary>
    /// 将事件封装为信封并发送到对应的事件组。
    /// </summary>
    public Task PublishAsync(string roomId, string eventType, object payload)
    {
        var envelope = new RoomEventEnvelope
        {
            RoomId = roomId,
            EventType = eventType,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Payload = JsonSerializer.SerializeToElement(payload, JsonOptions)
        };

        return _hubContext.Clients.Group(GameHub.BuildEventGroupName(roomId, eventType))
            .SendAsync(GameHub.RoomEventMethodName, envelope);
    }
}
