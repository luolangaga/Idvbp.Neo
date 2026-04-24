using System;
using System.Text.Json;
using System.Threading.Tasks;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Idvbp.Neo.Server.Services;

public interface IRoomEventPublisher
{
    Task PublishAsync(string roomId, string eventType, object payload);
}

public sealed class RoomEventPublisher : IRoomEventPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHubContext<GameHub> _hubContext;

    public RoomEventPublisher(IHubContext<GameHub> hubContext)
    {
        _hubContext = hubContext;
    }

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
