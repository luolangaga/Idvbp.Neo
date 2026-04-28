using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Idvbp.Neo.Server.Contracts;

namespace Idvbp.Neo.Client;

public sealed class RoomRealtimeClient : IAsyncDisposable
{
    private readonly SignalRClient _signalRClient;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    public RoomRealtimeClient(BpApiClient apiClient)
    {
        var hubUrl = new Uri(apiClient.BaseAddress, "hubs/game").ToString();
        _signalRClient = new SignalRClient(hubUrl);
        _signalRClient.OnRoomEvent(HandleRoomEvent);
        _signalRClient.Reconnected += _ => Reconnected?.Invoke() ?? Task.CompletedTask;
    }

    public event Action<RoomEventEnvelope>? RoomEventReceived;

    public event Func<Task>? Reconnected;

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

    public async ValueTask DisposeAsync()
    {
        await _signalRClient.DisposeAsync();
        _connectionGate.Dispose();
    }

    private void HandleRoomEvent(RoomEventEnvelope envelope)
    {
        RoomEventReceived?.Invoke(envelope);
    }
}
