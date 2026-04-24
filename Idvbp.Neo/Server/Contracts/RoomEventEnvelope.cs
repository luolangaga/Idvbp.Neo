using System;
using System.Text.Json;

namespace Idvbp.Neo.Server.Contracts;

public class RoomEventEnvelope
{
    public string EventType { get; init; } = string.Empty;
    public string RoomId { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public JsonElement Payload { get; init; }
}
