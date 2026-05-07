using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Server.Services;
using Idvbp.Neo.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 设置页面视图模型。
/// </summary>
public partial class SettingPageViewModel : ViewModelBase
{
    private const string RepositoryUrl = "https://github.com/AyaSlinc/Idvbp.Neo";
    private const string ContributorsApiUrl = "https://api.github.com/repos/AyaSlinc/Idvbp.Neo/contributors?per_page=12";
    private static readonly HttpClient GitHubClient = CreateGitHubClient();

    private readonly IOfficialCharacterModelService _officialCharacterModelService;
    private readonly IServiceProvider _serviceProvider;
    private CancellationTokenSource? _modelDownloadCts;
    private CancellationTokenSource? _contributorsCts;

    [ObservableProperty]
    private string _appVersion = BuildAppVersion();

    [ObservableProperty]
    private string _copyrightText = $"Copyright (C) {DateTime.Now.Year} AyaSlinc and Idvbp.Neo contributors.";

    [ObservableProperty]
    private bool _isModelDownloading;

    [ObservableProperty]
    private double _modelDownloadProgress;

    [ObservableProperty]
    private string _modelDownloadProgressText = "未开始";

    [ObservableProperty]
    private string _modelDownloadStageText = "官方模型会下载到 wwwroot/official-models。";

    [ObservableProperty]
    private ObservableCollection<ContributorViewModel> _contributors = [];

    [ObservableProperty]
    private bool _isLoadingContributors;

    [ObservableProperty]
    private string _contributorsStatus = "正在从 GitHub 加载贡献者信息...";

    [ObservableProperty]
    private string _debugStatus = "调试操作会立即影响当前进程，仅用于验证诊断流程。";

    public SettingPageViewModel(IOfficialCharacterModelService officialCharacterModelService, IServiceProvider serviceProvider)
    {
        _officialCharacterModelService = officialCharacterModelService;
        _serviceProvider = serviceProvider;
        _ = RefreshContributorsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanEnsureOfficialModels))]
    private async Task EnsureOfficialModelsAsync()
    {
        _modelDownloadCts?.Cancel();
        _modelDownloadCts?.Dispose();
        _modelDownloadCts = new CancellationTokenSource();
        IsModelDownloading = true;
        ModelDownloadProgress = 0;
        ModelDownloadProgressText = "准备中";
        ModelDownloadStageText = "正在连接官方资源列表...";

        var progress = new Progress<OfficialModelDownloadProgress>(item =>
        {
            ModelDownloadProgress = item.Total <= 0 ? 0 : item.Current * 100d / item.Total;
            ModelDownloadProgressText = $"{item.Current}/{item.Total}";
            ModelDownloadStageText = item.Status switch
            {
                "downloaded" => $"已下载 {item.ModelName}",
                "cached" => $"已存在 {item.ModelName}",
                "failed" => $"失败: {item.ModelName} - {item.Error}",
                _ => $"正在补齐: {item.ModelName}"
            };
        });

        try
        {
            var summary = await _officialCharacterModelService.EnsureAllModelsAsync(progress, _modelDownloadCts.Token);
            ModelDownloadProgress = 100;
            ModelDownloadProgressText = $"{summary.Total}/{summary.Total}";
            ModelDownloadStageText = $"补齐完成: 新下载 {summary.Downloaded}, 已存在 {summary.Cached}, 失败 {summary.Failed}";
        }
        catch (OperationCanceledException)
        {
            ModelDownloadStageText = "已取消官方模型补齐。";
        }
        catch (Exception ex)
        {
            ModelDownloadStageText = $"官方模型补齐失败: {ex.Message}";
        }
        finally
        {
            IsModelDownloading = false;
        }
    }

    private bool CanEnsureOfficialModels() => !IsModelDownloading;

    [RelayCommand(CanExecute = nameof(CanCancelOfficialModelDownload))]
    private void CancelOfficialModelDownload()
    {
        _modelDownloadCts?.Cancel();
    }

    private bool CanCancelOfficialModelDownload() => IsModelDownloading;

    partial void OnIsModelDownloadingChanged(bool value)
    {
        EnsureOfficialModelsCommand.NotifyCanExecuteChanged();
        CancelOfficialModelDownloadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task RefreshContributorsAsync()
    {
        _contributorsCts?.Cancel();
        _contributorsCts?.Dispose();
        _contributorsCts = new CancellationTokenSource();

        IsLoadingContributors = true;
        ContributorsStatus = "正在从 GitHub 拉取开发者贡献榜...";
        Contributors.Clear();

        try
        {
            var contributors = await GitHubClient.GetFromJsonAsync<GitHubContributor[]>(
                ContributorsApiUrl,
                _contributorsCts.Token) ?? [];

            foreach (var contributor in contributors)
            {
                var profile = await LoadProfileAsync(contributor.Login, _contributorsCts.Token);
                var item = new ContributorViewModel
                {
                    Login = contributor.Login,
                    Name = profile?.Name,
                    Bio = string.IsNullOrWhiteSpace(profile?.Bio) ? "这个开发者还没有公开简介。" : profile.Bio,
                    Contributions = contributor.Contributions,
                    HtmlUrl = contributor.HtmlUrl,
                    Avatar = await LoadAvatarAsync(contributor.AvatarUrl, _contributorsCts.Token)
                };

                Contributors.Add(item);
            }

            ContributorsStatus = Contributors.Count == 0
                ? "GitHub 暂未返回贡献者信息。"
                : $"已加载 {Contributors.Count} 位贡献者，按 GitHub 贡献数排序。";
        }
        catch (OperationCanceledException)
        {
            ContributorsStatus = "贡献者加载已取消。";
        }
        catch (Exception ex)
        {
            ContributorsStatus = $"贡献者加载失败: {ex.Message}";
        }
        finally
        {
            IsLoadingContributors = false;
        }
    }

    [RelayCommand]
    private void OpenRepository() => OpenPath(RepositoryUrl);

    [RelayCommand]
    private void OpenConfigDirectory()
    {
        var configDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(configDir);
        OpenPath(configDir);
    }

    [RelayCommand]
    private void OpenLogDirectory()
    {
        var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs", "runtime");
        Directory.CreateDirectory(logDir);
        OpenPath(logDir);
    }

    [RelayCommand]
    private void OpenLogViewer()
    {
        var viewModel = _serviceProvider.GetRequiredService<LogViewerViewModel>();
        var window = new LogViewerWindow(viewModel);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            window.Show(desktop.MainWindow);
        }
        else
        {
            window.Show();
        }
    }

    [RelayCommand]
    private void ManualGc()
    {
        var before = GC.GetTotalMemory(false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var after = GC.GetTotalMemory(true);
        DebugStatus = $"GC 已执行。内存: {FormatBytes(before)} -> {FormatBytes(after)}";
    }

    [RelayCommand]
    private static void ThrowTestError()
    {
        throw new InvalidOperationException("这是从设置页调试面板手动抛出的测试错误。");
    }

    private static async Task<GitHubUser?> LoadProfileAsync(string login, CancellationToken cancellationToken)
    {
        try
        {
            return await GitHubClient.GetFromJsonAsync<GitHubUser>(
                $"https://api.github.com/users/{Uri.EscapeDataString(login)}",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<Bitmap?> LoadAvatarAsync(string avatarUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            return null;
        }

        try
        {
            var bytes = await GitHubClient.GetByteArrayAsync(avatarUrl, cancellationToken);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    private static void OpenPath(string pathOrUrl)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = pathOrUrl,
            UseShellExecute = true
        });
    }

    private static string BuildAppVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version
            ?? Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "Version unknown" : $"Version {version}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static HttpClient CreateGitHubClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Idvbp.Neo");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed record GitHubContributor(
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("avatar_url")] string AvatarUrl,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("contributions")] int Contributions);

    private sealed record GitHubUser(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("bio")] string? Bio);
}

public partial class ContributorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _login = "";

    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private string _bio = "";

    [ObservableProperty]
    private int _contributions;

    [ObservableProperty]
    private string _htmlUrl = "";

    [ObservableProperty]
    private Bitmap? _avatar;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Login : Name;

    public string ContributionsText => $"{Contributions} commits";

    [RelayCommand]
    private void OpenProfile()
    {
        if (string.IsNullOrWhiteSpace(HtmlUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = HtmlUrl,
            UseShellExecute = true
        });
    }
}
