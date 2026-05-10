using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Idvbp.Neo.Server.Contracts;

namespace Idvbp.Neo.Client;

/// <summary>
/// SignalR 客户端封装，提供连接管理与房间事件订阅功能。
/// </summary>
public class SignalRClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly TimeSpan _defaultInvokeTimeout;

    /// <summary>
    /// 获取底层 HubConnection 实例。
    /// </summary>
    public HubConnection Connection => _connection;

    /// <summary>
    /// 获取当前连接 ID。
    /// </summary>
    public string ConnectionId => _connection.ConnectionId ?? string.Empty;

    /// <summary>
    /// 获取当前连接状态。
    /// </summary>
    public HubConnectionState State => _connection.State;

    /// <summary>
    /// 重连中事件。
    /// </summary>
    public event Func<string, Task>? Reconnecting;

    /// <summary>
    /// 重连成功事件。
    /// </summary>
    public event Func<string?, Task>? Reconnected;

    /// <summary>
    /// 连接关闭事件。
    /// </summary>
    public event Func<Exception?, Task>? Closed;

    /// <summary>
    /// 初始化 SignalR 客户端。
    /// </summary>
    /// <param name="url">SignalR 中心地址。</param>
    public SignalRClient(string url)
    {
        _defaultInvokeTimeout = TimeSpan.FromSeconds(10);
        _connection = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect()
            .Build();

        _connection.Reconnecting += error =>
        {
            Reconnecting?.Invoke(error?.Message ?? "unknown");
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            Reconnected?.Invoke(connectionId);
            return Task.CompletedTask;
        };

        _connection.Closed += error =>
        {
            Closed?.Invoke(error);
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// 启动连接，等待连接达到 Connected 状态。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_connection.State == HubConnectionState.Connected)
            return;

        if (_connection.State == HubConnectionState.Disconnected)
        {
            await _connection.StartAsync(cancellationToken);
            return;
        }

        // 状态为 Reconnecting：等待重连完成或取消
        var tcs = new TaskCompletionSource<bool>();
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        Func<string?, Task> reconnectedHandler = _ =>
        {
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        };

        _connection.Reconnected += reconnectedHandler;
        try
        {
            if (_connection.State == HubConnectionState.Connected)
                return;

            await tcs.Task;
        }
        finally
        {
            _connection.Reconnected -= reconnectedHandler;
        }
    }

    /// <summary>
    /// 停止连接。
    /// </summary>
    public async Task StopAsync()
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.StopAsync();
        }
    }

    /// <summary>
    /// 注册泛型事件处理器。
    /// </summary>
    public IDisposable On<T>(string methodName, Action<T> handler)
    {
        return _connection.On(methodName, handler);
    }

    /// <summary>
    /// 注册无参事件处理器。
    /// </summary>
    public IDisposable On(string methodName, Action handler)
    {
        return _connection.On(methodName, handler);
    }

    /// <summary>
    /// 注册双参数事件处理器。
    /// </summary>
    public IDisposable On<T1, T2>(string methodName, Action<T1, T2> handler)
    {
        return _connection.On(methodName, handler);
    }

    /// <summary>
    /// 注册房间事件处理器。
    /// </summary>
    public IDisposable OnRoomEvent(Action<RoomEventEnvelope> handler)
    {
        return _connection.On(GameHubMethods.RoomEvent, handler);
    }

    public IDisposable OnCurrentRoomChanged(Action<CurrentRoomPayload> handler)
    {
        return _connection.On(GameHubMethods.CurrentRoomChanged, handler);
    }

    /// <summary>
    /// 调用服务端方法（单参数）。连接未就绪时静默跳过。
    /// </summary>
    public async Task InvokeAsync(string methodName, object? arg1 = null)
    {
        if (_connection.State == HubConnectionState.Connected)
            await _connection.InvokeAsync(methodName, arg1);
    }

    /// <summary>
    /// 调用服务端方法（双参数）。连接未就绪时静默跳过。
    /// </summary>
    public async Task InvokeAsync(string methodName, object? arg1, object? arg2)
    {
        if (_connection.State == HubConnectionState.Connected)
            await _connection.InvokeAsync(methodName, arg1, arg2);
    }

    /// <summary>
    /// 调用服务端方法并返回结果（单参数）。连接未就绪时返回默认值。
    /// </summary>
    public async Task<T?> InvokeAsync<T>(string methodName, object? arg1 = null)
    {
        if (_connection.State == HubConnectionState.Connected)
            return await _connection.InvokeAsync<T>(methodName, arg1);
        return default;
    }

    public async Task<T?> InvokeResultAsync<T>(string methodName)
    {
        if (_connection.State == HubConnectionState.Connected)
            return await _connection.InvokeAsync<T>(methodName);
        return default;
    }

    /// <summary>
    /// 调用服务端方法并返回结果（双参数）。连接未就绪时返回默认值。
    /// </summary>
    public async Task<T?> InvokeAsync<T>(string methodName, object? arg1, object? arg2)
    {
        if (_connection.State == HubConnectionState.Connected)
            return await _connection.InvokeAsync<T>(methodName, arg1, arg2);
        return default;
    }

    /// <summary>
    /// 加入房间。
    /// </summary>
    public Task JoinRoomAsync(string roomId) => InvokeAsync(GameHubMethods.JoinRoom, roomId);

    /// <summary>
    /// 离开房间。
    /// </summary>
    public Task LeaveRoomAsync(string roomId) => InvokeAsync(GameHubMethods.LeaveRoom, roomId);

    /// <summary>
    /// 替换事件订阅。
    /// </summary>
    public Task<IReadOnlyCollection<string>?> ReplaceSubscriptionsAsync(string roomId, IEnumerable<string> eventTypes)
        => InvokeAsync<IReadOnlyCollection<string>>(GameHubMethods.ReplaceSubscriptions, roomId, eventTypes);

    /// <summary>
    /// 订阅事件。
    /// </summary>
    public Task<IReadOnlyCollection<string>?> SubscribeToEventsAsync(string roomId, IEnumerable<string> eventTypes)
        => InvokeAsync<IReadOnlyCollection<string>>(GameHubMethods.SubscribeToEvents, roomId, eventTypes);

    /// <summary>
    /// 取消订阅事件。
    /// </summary>
    public Task<IReadOnlyCollection<string>?> UnsubscribeFromEventsAsync(string roomId, IEnumerable<string> eventTypes)
        => InvokeAsync<IReadOnlyCollection<string>>(GameHubMethods.UnsubscribeFromEvents, roomId, eventTypes);

    /// <summary>
    /// 请求房间快照。
    /// </summary>
    public Task RequestRoomSnapshotAsync(string roomId) => InvokeAsync(GameHubMethods.RequestRoomSnapshot, roomId);

    public Task<CurrentRoomPayload?> RequestCurrentRoomAsync()
        => InvokeResultAsync<CurrentRoomPayload>(GameHubMethods.RequestCurrentRoom);

    public Task<CurrentRoomPayload?> SetCurrentRoomAsync(string? roomId)
        => InvokeAsync<CurrentRoomPayload>(GameHubMethods.SetCurrentRoom, roomId);

    /// <summary>
    /// 获取可用事件类型列表。
    /// </summary>
    public Task<IReadOnlyCollection<string>?> GetAvailableEventTypesAsync()
        => InvokeResultAsync<IReadOnlyCollection<string>>(GameHubMethods.GetAvailableEventTypes);

    /// <summary>
    /// 异步释放连接资源。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _connection.StopAsync();
        }
        catch
        {
        }

        await _connection.DisposeAsync();
    }
}

/// <summary>
/// SignalR 游戏中心方法名称常量。
/// </summary>
public static class GameHubMethods
{
    public const string JoinRoom = "JoinRoom";
    public const string LeaveRoom = "LeaveRoom";
    public const string ReplaceSubscriptions = "ReplaceSubscriptions";
    public const string SubscribeToEvents = "SubscribeToEvents";
    public const string UnsubscribeFromEvents = "UnsubscribeFromEvents";
    public const string RequestRoomSnapshot = "RequestRoomSnapshot";
    public const string RequestCurrentRoom = "RequestCurrentRoom";
    public const string SetCurrentRoom = "SetCurrentRoom";
    public const string GetAvailableEventTypes = "GetAvailableEventTypes";
    public const string RoomEvent = "RoomEvent";
    public const string CurrentRoomChanged = "CurrentRoomChanged";
}
