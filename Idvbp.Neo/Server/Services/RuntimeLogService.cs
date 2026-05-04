using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Idvbp.Neo.Server.Services;

public interface IRuntimeLogService
{
    void Add(string source, string level, string message);
    IReadOnlyList<RuntimeLogEntry> GetRecent(int count = 200);
    void Clear();
}

public sealed class RuntimeLogEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Source { get; set; } = "";
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class FileRuntimeLogService : IRuntimeLogService
{
    private readonly ConcurrentBag<RuntimeLogEntry> _logs = [];
    private readonly string _logDirectory;
    private readonly object _fileLock = new();
    private long _sequence;
    private string _currentFilePath = "";
    private DateTime _currentFileDate;

    public FileRuntimeLogService(string logDirectory)
    {
        _logDirectory = Path.GetFullPath(logDirectory);
        Directory.CreateDirectory(_logDirectory);
        EnsureFile();
    }

    public void Add(string source, string level, string message)
    {
        var entry = new RuntimeLogEntry
        {
            Id = System.Threading.Interlocked.Increment(ref _sequence),
            Timestamp = DateTime.Now,
            Source = source ?? "",
            Level = level ?? "info",
            Message = message ?? ""
        };
        _logs.Add(entry);

        var line = FormatLine(entry);
        Console.WriteLine(line);
        AppendToFile(line);
    }

    public IReadOnlyList<RuntimeLogEntry> GetRecent(int count = 200)
    {
        return _logs
            .OrderByDescending(x => x.Id)
            .Take(count)
            .OrderBy(x => x.Id)
            .ToList();
    }

    public void Clear()
    {
        while (_logs.TryTake(out _)) { }
        System.Threading.Interlocked.Exchange(ref _sequence, 0);
    }

    private void EnsureFile()
    {
        var today = DateTime.Now.Date;
        if (_currentFileDate == today && !string.IsNullOrEmpty(_currentFilePath))
        {
            return;
        }
        _currentFileDate = today;
        _currentFilePath = Path.Combine(_logDirectory, $"runtime-{today:yyyyMMdd}.log");
    }

    private static string FormatLine(RuntimeLogEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(entry.Timestamp.ToString("HH:mm:ss.fff")).Append("] ");
        sb.Append('[').Append(entry.Level.ToUpperInvariant()).Append("] ");
        sb.Append('[').Append(entry.Source).Append("] ");
        sb.Append(entry.Message);
        return sb.ToString();
    }

    private void AppendToFile(string line)
    {
        EnsureFile();
        lock (_fileLock)
        {
            try
            {
                File.AppendAllText(_currentFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* 文件写入失败不影响主流程 */ }
        }
    }
}
