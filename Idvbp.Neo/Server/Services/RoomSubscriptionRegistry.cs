using System.Collections.Generic;
using System.Linq;

namespace Idvbp.Neo.Server.Services;

/// <summary>
/// 房间订阅注册表，管理 SignalR 连接与房间事件订阅的映射关系。
/// </summary>
public sealed class RoomSubscriptionRegistry
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _subscriptions = new();

    /// <summary>
    /// 替换指定连接在指定房间的订阅事件类型。
    /// </summary>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="roomId">房间标识。</param>
    /// <param name="eventTypes">新的事件类型集合。</param>
    /// <returns>规范化后的事件类型集合。</returns>
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

    /// <summary>
    /// 获取指定连接在指定房间的订阅事件类型。
    /// </summary>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="roomId">房间标识。</param>
    /// <returns>订阅的事件类型集合。</returns>
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

    /// <summary>
    /// 移除指定连接在指定房间的订阅。
    /// </summary>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="roomId">房间标识。</param>
    /// <returns>被移除的事件类型集合。</returns>
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

    /// <summary>
    /// 移除指定连接的所有订阅。
    /// </summary>
    /// <param name="connectionId">连接标识。</param>
    /// <returns>该连接的所有房间订阅映射。</returns>
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
