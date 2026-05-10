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

    public RoomRealtimeClient(BpApiClient apiClient)
    {
        var hubUrl = new Uri(apiClient.BaseAddress, "hubs/game").ToString();
        _signalRClient = new SignalRClient(hubUrl);
        _signalRClient.OnRoomEvent(HandleRoomEvent);
        _signalRClient.OnCurrentRoomChanged(HandleCurrentRoomChanged);
        _signalRClient.Reconnected += _ => Reconnected?.Invoke() ?? Task.CompletedTask;
    }

    public event Action<RoomEventEnvelope>? RoomEventReceived;

    public event Action<CurrentRoomPayload>? CurrentRoomChanged;

    public event Func<Task>? Reconnected;

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (_signalRClient.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
            return;

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (_signalRClient.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
                return;

            await _signalRClient.StartAsync(cancellationToken);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task SubscribeToRoomAsync(string roomId, IEnumerable<string> eventTypes, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await _signalRClient.JoinRoomAsync(roomId);
        await _signalRClient.ReplaceSubscriptionsAsync(roomId, eventTypes);
    }

    public Task LeaveRoomAsync(string roomId)
        => _signalRClient.LeaveRoomAsync(roomId);

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
