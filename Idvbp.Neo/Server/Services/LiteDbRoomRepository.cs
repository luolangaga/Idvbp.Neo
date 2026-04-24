using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using Idvbp.Neo.Models;

namespace Idvbp.Neo.Server.Services;

public interface IRoomRepository
{
    IReadOnlyCollection<BpRoom> GetAll();
    BpRoom? GetById(string roomId);
    bool Exists(string roomId);
    void Upsert(BpRoom room);
}

public sealed class LiteDbRoomRepository : IRoomRepository, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<BpRoom> _rooms;

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

    public IReadOnlyCollection<BpRoom> GetAll() => _rooms.FindAll().ToList();

    public BpRoom? GetById(string roomId) => _rooms.FindById(roomId);

    public bool Exists(string roomId) => _rooms.Exists(x => x.RoomId == roomId);

    public void Upsert(BpRoom room)
    {
        _rooms.Upsert(room);
        _database.Checkpoint();
    }

    public void Dispose()
    {
        _database.Dispose();
    }
}
