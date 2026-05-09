using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Core.Abstractions.Services;
using Idvbp.Neo.Server.Services;

namespace Idvbp.Neo.ViewModels.Pages;

public partial class LogViewerViewModel : ViewModelBase
{
    private readonly ISystemService _systemService;
    private readonly IClipboardService _clipboardService;

    private const string DefaultLogDirectory = "logs/runtime";
    private const string DefaultBugReportDirectory = "logs/bug-reports";
    private const string DefaultLogFilePattern = "runtime-*.log";
    private const string DefaultLogDateFormat = "yyyyMMdd";
    private const string LogLinePattern = @"^\[(\d{2}:\d{2}:\d{2}\.\d{3})\]\s+\[(\w+)\]\s+\[([^\]]*)\]\s+(.*)$";
    private const string DefaultGitHubIssueUrl = "https://github.com/AyaSlinc/Idvbp.Neo/issues/new?title={0}&body={1}";

    private static readonly string[] DefaultLevelOptions = ["全部", "ERROR", "WARN", "INFO", "DEBUG", "TRACE"];

    [ObservableProperty]
    private ObservableCollection<LogViewerLogItem> _logs = [];

    [ObservableProperty]
    private ObservableCollection<LogViewerLogItem> _filteredLogs = [];

    [ObservableProperty]
    private string _searchKeyword = "";

    [ObservableProperty]
    private string? _selectedLevel = "全部";

    [ObservableProperty]
    private DateTimeOffset? _startDate;

    [ObservableProperty]
    private DateTimeOffset? _endDate;

    [ObservableProperty]
    private int _totalLogCount;

    [ObservableProperty]
    private int _filteredCount;

    [ObservableProperty]
    private LogViewerLogItem? _selectedLog;

    [ObservableProperty]
    private ObservableCollection<RuntimeLogEntry> _selectedLogs = [];

    [ObservableProperty]
    private int _selectedLogCount;

    [ObservableProperty]
    private string _operationStatus = "选择日志后可复制、打包或提交 Issue";

    [ObservableProperty]
    private string _lastPackagePath = "";

    public ObservableCollection<string> LevelOptions { get; } = [.. DefaultLevelOptions];

    public LogViewerViewModel(ISystemService systemService, IClipboardService clipboardService)
    {
        _systemService = systemService;
        _clipboardService = clipboardService;
        StartDate = DateTimeOffset.Now.Date.AddDays(-7);
        EndDate = DateTimeOffset.Now.Date.AddDays(1);
        LoadLogs();
    }

    [RelayCommand]
    private void LoadLogs()
    {
        var logsDir = Path.Combine(_systemService.GetCurrentDirectory(), DefaultLogDirectory);
        var allLogs = ReadLogsFromDirectory(logsDir);
        Logs.Clear();
        foreach (var log in allLogs)
        {
            var item = new LogViewerLogItem(log);
            item.SelectionChanged += OnLogItemSelectionChanged;
            Logs.Add(item);
        }
        TotalLogCount = Logs.Count;
        ApplyFilters();
        OperationStatus = $"已加载 {TotalLogCount} 条日志";
    }

    public void SetSelectedLogs(IEnumerable<RuntimeLogEntry> selectedLogs)
    {
        var selectedSet = selectedLogs.ToHashSet();
        foreach (var item in Logs)
        {
            item.IsSelected = selectedSet.Contains(item.Entry);
        }

        SyncCheckedLogs();
    }

    [RelayCommand]
    private void SelectAllFilteredLogs()
    {
        foreach (var item in FilteredLogs)
        {
            item.IsSelected = true;
        }

        SyncCheckedLogs();
    }

    [RelayCommand]
    private void ClearSelectedLogs()
    {
        foreach (var item in Logs)
        {
            item.IsSelected = false;
        }

        SyncCheckedLogs();
    }

    private void SyncCheckedLogs()
    {
        SelectedLogs.Clear();
        foreach (var log in Logs.Where(x => x.IsSelected).Select(x => x.Entry).OrderBy(x => x.Timestamp))
        {
            SelectedLogs.Add(log);
        }

        SelectedLogCount = SelectedLogs.Count;
        OperationStatus = SelectedLogCount > 0 ? $"已勾选 {SelectedLogCount} 条日志" : "勾选日志后可复制、打包或提交 Issue";
        CopySelectedLogsCommand.NotifyCanExecuteChanged();
        PackageSelectedLogsCommand.NotifyCanExecuteChanged();
        ReportBugCommand.NotifyCanExecuteChanged();
    }

    private List<RuntimeLogEntry> ReadLogsFromDirectory(string directory)
    {
        var entries = new List<RuntimeLogEntry>();
        if (!Directory.Exists(directory))
            return entries;

        var files = Directory.GetFiles(directory, DefaultLogFilePattern).OrderByDescending(f => f).ToList();
        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var dateStr = fileName.Replace("runtime-", "");
            if (!DateTime.TryParseExact(dateStr, DefaultLogDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate))
                continue;

            try
            {
                var lines = File.ReadAllLines(file);
                foreach (var line in lines)
                {
                    var entry = ParseLogLine(line, fileDate);
                    if (entry != null)
                        entries.Add(entry);
                }
            }
            catch
            {
            }
        }
        return entries;
    }

    private static RuntimeLogEntry? ParseLogLine(string line, DateTime fileDate)
    {
        var match = Regex.Match(line, LogLinePattern);
        if (!match.Success)
            return null;

        var timeStr = match.Groups[1].Value;
        if (!TimeSpan.TryParse(timeStr, out var time))
            return null;

        return new RuntimeLogEntry
        {
            Timestamp = fileDate.Add(time),
            Source = match.Groups[3].Value,
            Level = match.Groups[2].Value,
            Message = match.Groups[4].Value
        };
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLogs))]
    private async Task CopySelectedLogsAsync()
    {
        await _clipboardService.SetTextAsync(FormatLogs(SelectedLogs));
        OperationStatus = $"已复制 {SelectedLogCount} 条日志到剪贴板";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLogs))]
    private void PackageSelectedLogs()
    {
        LastPackagePath = PackageSelectedLogsCore();
        OperationStatus = $"已打包 {SelectedLogCount} 条日志：{LastPackagePath}";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLogs))]
    private void ReportBug()
    {
        LastPackagePath = PackageSelectedLogsCore();
        var logsText = FormatLogs(SelectedLogs);
        if (logsText.Length > 12000)
        {
            logsText = logsText[..12000] + Environment.NewLine + "...日志过长，完整内容请附加 zip 文件...";
        }

        var title = Uri.EscapeDataString("Bug report from Idvbp.Neo log viewer");
        var body = Uri.EscapeDataString($"""
        ## 问题描述
        请在这里描述你遇到的问题。

        ## 日志附件
        已在本机生成日志包：{LastPackagePath}
        GitHub 网页无法自动上传本地文件，请在提交前手动拖入该 zip 文件。

        ## 选中日志预览
        ```text
        {logsText}
        ```
        """);

        _systemService.OpenUrl(string.Format(DefaultGitHubIssueUrl, title, body));
        OperationStatus = $"已打开 GitHub Issue 页面，日志包：{LastPackagePath}";
    }

    private bool HasSelectedLogs() => SelectedLogCount > 0;

    private string PackageSelectedLogsCore()
    {
        var outputDir = Path.Combine(_systemService.GetCurrentDirectory(), DefaultBugReportDirectory);
        Directory.CreateDirectory(outputDir);

        var packagePath = Path.Combine(outputDir, $"idvbp-neo-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("selected-logs.txt", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(FormatLogs(SelectedLogs));
        return packagePath;
    }

    private static string FormatLogs(IEnumerable<RuntimeLogEntry> logs)
    {
        var builder = new StringBuilder();
        foreach (var log in logs.OrderBy(x => x.Timestamp))
        {
            builder
                .Append('[').Append(log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("] ")
                .Append('[').Append(log.Level.ToUpperInvariant()).Append("] ")
                .Append('[').Append(log.Source).Append("] ")
                .AppendLine(log.Message);
        }

        return builder.ToString();
    }

    [RelayCommand]
    private void ApplyFilters()
    {
        var query = Logs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            var keyword = SearchKeyword.ToLowerInvariant();
            query = query.Where(x =>
                x.Message.ToLowerInvariant().Contains(keyword) ||
                x.Source.ToLowerInvariant().Contains(keyword) ||
                x.Level.ToLowerInvariant().Contains(keyword));
        }

        if (SelectedLevel != "全部")
        {
            query = query.Where(x => x.Level.Equals(SelectedLevel, StringComparison.OrdinalIgnoreCase));
        }

        if (StartDate.HasValue)
        {
            var start = StartDate.Value.DateTime.Date;
            query = query.Where(x => x.Timestamp >= start);
        }

        if (EndDate.HasValue)
        {
            var end = EndDate.Value.DateTime.Date.AddDays(1);
            query = query.Where(x => x.Timestamp < end);
        }

        var filtered = query.OrderByDescending(x => x.Timestamp).ToList();
        FilteredLogs.Clear();
        foreach (var log in filtered)
        {
            FilteredLogs.Add(log);
        }
        FilteredCount = FilteredLogs.Count;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchKeyword = "";
        SelectedLevel = "全部";
        StartDate = DateTimeOffset.Now.Date.AddDays(-7);
        EndDate = DateTimeOffset.Now.Date.AddDays(1);
        ApplyFilters();
    }

    [RelayCommand]
    private void OpenLogDirectory()
    {
        var logDir = Path.Combine(_systemService.GetCurrentDirectory(), DefaultLogDirectory);
        if (Directory.Exists(logDir))
        {
            _systemService.OpenPath(logDir);
        }
    }

    [RelayCommand]
    private void RefreshLogs()
    {
        LoadLogs();
    }

    partial void OnSearchKeywordChanged(string value) => ApplyFilters();
    partial void OnSelectedLevelChanged(string? value) => ApplyFilters();
    partial void OnStartDateChanged(DateTimeOffset? value) => ApplyFilters();
    partial void OnEndDateChanged(DateTimeOffset? value) => ApplyFilters();

    protected override void Dispose(bool disposing)
    {
        if (!disposing || IsDisposed)
        {
            return;
        }

        foreach (var item in Logs)
        {
            item.SelectionChanged -= OnLogItemSelectionChanged;
        }

        Logs.Clear();

        base.Dispose(disposing);
    }

    private void OnLogItemSelectionChanged(object? sender, EventArgs e) => SyncCheckedLogs();
}

public partial class LogViewerLogItem : ObservableObject
{
    public LogViewerLogItem(RuntimeLogEntry entry)
    {
        Entry = entry;
    }

    public event EventHandler? SelectionChanged;

    public RuntimeLogEntry Entry { get; }

    public DateTime Timestamp => Entry.Timestamp;

    public string Source => Entry.Source;

    public string Level => Entry.Level;

    public string Message => Entry.Message;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
}
