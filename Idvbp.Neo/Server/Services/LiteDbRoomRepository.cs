using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using Idvbp.Neo.Models;

namespace Idvbp.Neo.Server.Services;

/// <summary>
/// 房间仓储接口，定义房间数据的持久化操作。
/// </summary>
public interface IRoomRepository
{
    /// <summary>
    /// 获取所有房间。
    /// </summary>
    IReadOnlyCollection<BpRoom> GetAll();

    /// <summary>
    /// 根据 ID 获取房间。
    /// </summary>
    BpRoom? GetById(string roomId);

    /// <summary>
    /// 判断房间是否存在。
    /// </summary>
    bool Exists(string roomId);

    /// <summary>
    /// 插入或更新房间。
    /// </summary>
    void Upsert(BpRoom room);
}

/// <summary>
/// 基于 LiteDB 的房间仓储实现。
/// </summary>
public sealed class LiteDbRoomRepository : IRoomRepository, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<BpRoom> _rooms;

    /// <summary>
    /// 初始化 LiteDB 房间仓储。
    /// </summary>
    /// <param name="databasePath">数据库文件路径。</param>
    public LiteDbRoomRepository(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var mapper = new BsonMapper();
        mapper.Entity<BpRoom>().Id(x => x.RoomId);
        _database = new LiteDatabase($"Filename={fullPath};Connection=shared", mapper);
        _rooms = _database.GetCollection<BpRoom>("rooms");
        _rooms.EnsureIndex(x => x.RoomName);
    }

    /// <summary>
    /// 获取所有房间。
    /// </summary>
    public IReadOnlyCollection<BpRoom> GetAll() => _rooms.FindAll().ToList();

    /// <summary>
    /// 根据 ID 获取房间。
    /// </summary>
    public BpRoom? GetById(string roomId) => _rooms.FindById(roomId);

    /// <summary>
    /// 判断房间是否存在。
    /// </summary>
    public bool Exists(string roomId) => _rooms.Exists(x => x.RoomId == roomId);

    /// <summary>
    /// 插入或更新房间，并执行检查点。
    /// </summary>
    public void Upsert(BpRoom room)
    {
        _rooms.Upsert(room);
        _database.Checkpoint();
    }

    /// <summary>
    /// 释放数据库资源。
    /// </summary>
    public void Dispose()
    {
        _database.Dispose();
    }
}
