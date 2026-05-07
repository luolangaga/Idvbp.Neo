using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Idvbp.Neo.Server.Contracts;

namespace Idvbp.Neo.Client;

/// <summary>
/// 房间实时客户端，封装 SignalR 连接与房间事件订阅。
/// </summary>
public sealed class RoomRealtimeClient : IAsyncDisposable
{
    private readonly SignalRClient _signalRClient;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    /// <summary>
    /// 初始化房间实时客户端。
    /// </summary>
    /// <param name="apiClient">BP API 客户端。</param>
    public RoomRealtimeClient(BpApiClient apiClient)
    {
        var hubUrl = new Uri(apiClient.BaseAddress, "hubs/game").ToString();
        _signalRClient = new SignalRClient(hubUrl);
        _signalRClient.OnRoomEvent(HandleRoomEvent);
        _signalRClient.OnCurrentRoomChanged(HandleCurrentRoomChanged);
        _signalRClient.Reconnected += _ => Reconnected?.Invoke() ?? Task.CompletedTask;
    }

    /// <summary>
    /// 房间事件接收事件。
    /// </summary>
    public event Action<RoomEventEnvelope>? RoomEventReceived;

    public event Action<CurrentRoomPayload>? CurrentRoomChanged;

    /// <summary>
    /// 重连成功事件。
    /// </summary>
    public event Func<Task>? Reconnected;

    /// <summary>
    /// 确保连接已建立。
    /// </summary>
    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            await _signalRClient.StartAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <summary>
    /// 订阅指定房间的事件。
    /// </summary>
    public async Task SubscribeToRoomAsync(string roomId, IEnumerable<string> eventTypes, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await _signalRClient.JoinRoomAsync(roomId);
        await _signalRClient.ReplaceSubscriptionsAsync(roomId, eventTypes);
    }

    /// <summary>
    /// 离开指定房间。
    /// </summary>
    public Task LeaveRoomAsync(string roomId)
        => _signalRClient.LeaveRoomAsync(roomId);

    /// <summary>
    /// 请求房间快照。
    /// </summary>
    public Task RequestRoomSnapshotAsync(string roomId)
        => _signalRClient.RequestRoomSnapshotAsync(roomId);

    public Task<CurrentRoomPayload?> RequestCurrentRoomAsync()
        => _signalRClient.RequestCurrentRoomAsync();

    public Task<CurrentRoomPayload?> SetCurrentRoomAsync(string? roomId)
        => _signalRClient.SetCurrentRoomAsync(roomId);

    /// <summary>
    /// 异步释放资源。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _signalRClient.DisposeAsync();
        _connectionGate.Dispose();
    }

    private void HandleRoomEvent(RoomEventEnvelope envelope)
    {
        RoomEventReceived?.Invoke(envelope);
    }

    private void HandleCurrentRoomChanged(CurrentRoomPayload payload)
    {
        CurrentRoomChanged?.Invoke(payload);
    }
}
