using System.Collections.Generic;
using System.Linq;

namespace Idvbp.Neo.Server.Services;

public sealed class RoomSubscriptionRegistry
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _subscriptions = new();

    public IReadOnlyCollection<string> Replace(string connectionId, string roomId, IEnumerable<string> eventTypes)
    {
        lock (_syncRoot)
        {
            if (!_subscriptions.TryGetValue(connectionId, out var roomSubscriptions))
            {
                roomSubscriptions = new Dictionary<string, HashSet<string>>();
                _subscriptions[connectionId] = roomSubscriptions;
            }

            var normalized = new HashSet<string>(eventTypes.Where(x => !string.IsNullOrWhiteSpace(x)));
            roomSubscriptions[roomId] = normalized;
            return normalized.ToArray();
        }
    }

    public IReadOnlyCollection<string> Get(string connectionId, string roomId)
    {
        lock (_syncRoot)
        {
            if (_subscriptions.TryGetValue(connectionId, out var roomSubscriptions) &&
                roomSubscriptions.TryGetValue(roomId, out var eventTypes))
            {
                return eventTypes.ToArray();
            }

            return [];
        }
    }

    public IReadOnlyCollection<string> RemoveRoom(string connectionId, string roomId)
    {
        lock (_syncRoot)
        {
            if (!_subscriptions.TryGetValue(connectionId, out var roomSubscriptions) ||
                !roomSubscriptions.TryGetValue(roomId, out var eventTypes))
            {
                return [];
            }

            roomSubscriptions.Remove(roomId);
            if (roomSubscriptions.Count == 0)
            {
                _subscriptions.Remove(connectionId);
            }

            return eventTypes.ToArray();
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> RemoveConnection(string connectionId)
    {
        lock (_syncRoot)
        {
            if (!_subscriptions.TryGetValue(connectionId, out var roomSubscriptions))
            {
                return new Dictionary<string, IReadOnlyCollection<string>>();
            }

            _subscriptions.Remove(connectionId);
            return roomSubscriptions.ToDictionary(x => x.Key, x => (IReadOnlyCollection<string>)x.Value.ToArray());
        }
    }
}
