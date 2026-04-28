using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;

namespace Idvbp.Neo.Server.Services;

public interface IProxyPageConfigRepository
{
    IReadOnlyDictionary<string, string> GetAll();
    string GetValueOrDefault(string id);
    void Upsert(string id, string value);
}

public sealed class LiteDbProxyPageConfigRepository : IProxyPageConfigRepository, System.IDisposable
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<ProxyPageConfigEntry> _configs;

    public LiteDbProxyPageConfigRepository(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var mapper = new BsonMapper();
        mapper.Entity<ProxyPageConfigEntry>().Id(x => x.Id);
        _database = new LiteDatabase($"Filename={fullPath};Connection=shared", mapper);
        _configs = _database.GetCollection<ProxyPageConfigEntry>("proxy_page_configs");
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        return _configs.FindAll()
            .ToDictionary(item => item.Id, item => item.Value ?? string.Empty);
    }

    public string GetValueOrDefault(string id)
    {
        return _configs.FindById(id)?.Value ?? string.Empty;
    }

    public void Upsert(string id, string value)
    {
        _configs.Upsert(new ProxyPageConfigEntry
        {
            Id = id,
            Value = value
        });

        _database.Checkpoint();
    }

    public void Dispose()
    {
        _database.Dispose();
    }
}

public sealed class ProxyPageConfigEntry
{
    public string Id { get; set; } = "";
    public string Value { get; set; } = "";
}
