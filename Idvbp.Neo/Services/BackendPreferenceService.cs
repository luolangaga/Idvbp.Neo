using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Idvbp.Neo.Services;

public sealed class BackendPreferenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private BackendPreference? _cached;

    public BackendPreferenceService()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "backend-preference.json");
    }

    public async Task<BackendPreference> GetAsync()
    {
        if (_cached is not null)
            return _cached;

        await _lock.WaitAsync();
        try
        {
            if (_cached is not null)
                return _cached;

            if (!File.Exists(_filePath))
            {
                _cached = new BackendPreference { BackendMode = BackendMode.NotSet };
                return _cached;
            }

            var json = await File.ReadAllTextAsync(_filePath);
            _cached = JsonSerializer.Deserialize<BackendPreference>(json, JsonOptions)
                      ?? new BackendPreference { BackendMode = BackendMode.NotSet };
            return _cached;
        }
        catch
        {
            _cached = new BackendPreference { BackendMode = BackendMode.NotSet };
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAsync(BackendMode mode)
    {
        await _lock.WaitAsync();
        try
        {
            _cached = new BackendPreference { BackendMode = mode };
            var json = JsonSerializer.Serialize(_cached, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }
}

public sealed class BackendPreference
{
    public BackendMode BackendMode { get; set; } = BackendMode.NotSet;
}

public enum BackendMode
{
    NotSet = 0,
    Native = 1,
    Web = 2
}
