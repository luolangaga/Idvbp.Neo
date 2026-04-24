using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace Idvbp.Neo.Client;

public class SignalRClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public HubConnection Connection => _connection;
    public string ConnectionId => _connection.ConnectionId ?? string.Empty;
    public HubConnectionState State => _connection.State;

    public event Func<string, Task>? Reconnecting;
    public event Func<string?, Task>? Reconnected;
    public event Func<Exception?, Task>? Closed;

    public SignalRClient(string url)
    {
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

    public async Task StartAsync()
    {
        if (_connection.State == HubConnectionState.Disconnected)
        {
            await _connection.StartAsync();
        }
    }

    public async Task StopAsync()
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.StopAsync();
        }
    }

    public IDisposable On<T>(string methodName, Action<T> handler)
    {
        return _connection.On(methodName, handler);
    }

    public IDisposable On(string methodName, Action handler)
    {
        return _connection.On(methodName, handler);
    }

    public IDisposable On<T1, T2>(string methodName, Action<T1, T2> handler)
    {
        return _connection.On(methodName, handler);
    }

    public async Task InvokeAsync(string methodName, object? arg1 = null)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync(methodName, arg1);
        }
    }

    public async Task InvokeAsync(string methodName, object? arg1, object? arg2)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync(methodName, arg1, arg2);
        }
    }

    public async Task<T?> InvokeAsync<T>(string methodName, object? arg1 = null)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            return await _connection.InvokeAsync<T>(methodName, arg1);
        }
        return default;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
