using System;
using System.Text.Json;

namespace Idvbp.Neo.Server.Contracts;

/// <summary>
/// 房间事件信封，封装事件类型、房间 ID、发生时间与载荷数据。
/// </summary>
public class RoomEventEnvelope
{
    public string EventType { get; init; } = string.Empty;
    public string RoomId { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public JsonElement Payload { get; init; }
}
