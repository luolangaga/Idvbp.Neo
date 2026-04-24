using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Idvbp.Neo.Server.Hubs;

public class GameHub : Hub
{
    private static int _connectedClients;

    public override async Task OnConnectedAsync()
    {
        Interlocked.Increment(ref _connectedClients);
        await Clients.All.SendAsync("ClientCountChanged", _connectedClients);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Interlocked.Decrement(ref _connectedClients);
        await Clients.All.SendAsync("ClientCountChanged", _connectedClients);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinRoom(string roomName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        await Clients.Group(roomName).SendAsync("UserJoined", Context.ConnectionId, roomName);
    }

    public async Task LeaveRoom(string roomName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomName);
        await Clients.Group(roomName).SendAsync("UserLeft", Context.ConnectionId, roomName);
    }

    public async Task BroadcastToRoom(string roomName, string eventName, object data)
    {
        await Clients.Group(roomName).SendAsync(eventName, data);
    }

    public async Task BroadcastAll(string eventName, object data)
    {
        await Clients.All.SendAsync(eventName, data);
    }
}
