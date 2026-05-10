using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Core.Abstractions.Services;
using Idvbp.Neo.Server.Services;
using Idvbp.Neo.Services;
using Idvbp.Neo.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Idvbp.Neo.ViewModels.Pages;

public partial class SettingPageViewModel : ViewModelBase
{
    private const string DefaultRepositoryUrl = "https://github.com/AyaSlinc/Idvbp.Neo";
    private const string DefaultContributorsApiUrl = "https://api.github.com/repos/AyaSlinc/Idvbp.Neo/contributors?per_page=12";

    private readonly IOfficialCharacterModelService _officialCharacterModelService;
    private readonly IGitHubProxyService _gitHubProxyService;
    private readonly IServiceProvider _serviceProvider;
    private readonly AppNotificationService _notifications;
    private readonly ISystemService _systemService;
    private readonly IConfiguration _configuration;
    private readonly BackendPreferenceService _backendPreferenceService;
    private bool _isLoadingProxySelection;
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

    [ObservableProperty]
    private ObservableCollection<GitHubProxyEndpoint> _gitHubProxyEndpoints = [];

    [ObservableProperty]
    private GitHubProxyEndpoint? _selectedGitHubProxyEndpoint;

    [ObservableProperty]
    private string _gitHubProxyStatus = "";

    [ObservableProperty]
    private string _customGitHubProxyName = "";

    [ObservableProperty]
    private string _customGitHubProxyUrl = "";

    [ObservableProperty]
    private int _selectedBackendModeIndex;

    [ObservableProperty]
    private string _backendModeDescription = "";

    private string RepositoryUrl => _configuration["App:RepositoryUrl"] ?? DefaultRepositoryUrl;

    private string ContributorsApiUrl => _configuration["App:ContributorsApiUrl"] ?? DefaultContributorsApiUrl;

    public SettingPageViewModel(
        IOfficialCharacterModelService officialCharacterModelService,
        IGitHubProxyService gitHubProxyService,
        IServiceProvider serviceProvider,
        AppNotificationService notifications,
        ISystemService systemService,
        IConfiguration configuration,
        BackendPreferenceService backendPreferenceService)
    {
        _officialCharacterModelService = officialCharacterModelService;
        _gitHubProxyService = gitHubProxyService;
        _serviceProvider = serviceProvider;
        _notifications = notifications;
        _systemService = systemService;
        _configuration = configuration;
        _backendPreferenceService = backendPreferenceService;
        LoadGitHubProxyEndpoints();
        _ = RefreshContributorsAsync();
        _ = LoadBackendPreferenceAsync();
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
            _notifications.Success("官方模型补齐完成。");
        }
        catch (OperationCanceledException)
        {
            ModelDownloadStageText = "已取消官方模型补齐。";
            _notifications.Info("官方模型补齐已取消。");
        }
        catch (Exception ex)
        {
            ModelDownloadStageText = $"官方模型补齐失败: {ex.Message}";
            _notifications.Error(ex, "官方模型补齐失败");
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
            var contributors = await _gitHubProxyService.GetFromJsonAsync<GitHubContributor[]>(
                ContributorsApiUrl,
                _contributorsCts.Token) ?? [];

            foreach (var contributor in contributors)
            {
                var profile = await LoadProfileAsync(contributor.Login, _contributorsCts.Token);
                var item = new ContributorViewModel(_systemService)
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
            _notifications.Error(ex, "贡献者信息加载失败");
        }
        finally
        {
            IsLoadingContributors = false;
        }
    }

    [RelayCommand]
    private void OpenRepository() => _systemService.OpenUrl(_gitHubProxyService.RewriteUri(RepositoryUrl).ToString());

    [RelayCommand]
    private void OpenConfigDirectory()
    {
        var configDir = Path.Combine(_systemService.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(configDir);
        _systemService.OpenPath(configDir);
    }

    [RelayCommand]
    private void OpenLogDirectory()
    {
        var logDir = Path.Combine(_systemService.GetCurrentDirectory(), "logs", "runtime");
        Directory.CreateDirectory(logDir);
        _systemService.OpenPath(logDir);
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
    private async Task AddCustomGitHubProxyAsync()
    {
        try
        {
            var endpoint = await _gitHubProxyService.AddCustomEndpointAsync(CustomGitHubProxyName, CustomGitHubProxyUrl);
            CustomGitHubProxyName = "";
            CustomGitHubProxyUrl = "";
            LoadGitHubProxyEndpoints(endpoint.Id);
            GitHubProxyStatus = $"已添加并切换到 {endpoint.Name}";
            _notifications.Success("GitHub 代理已添加。");
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException)
        {
            GitHubProxyStatus = $"GitHub 代理添加失败: {ex.Message}";
            _notifications.Error(ex, "GitHub 代理添加失败");
        }
    }

    partial void OnSelectedGitHubProxyEndpointChanged(GitHubProxyEndpoint? value)
    {
        if (_isLoadingProxySelection || value is null)
        {
            return;
        }

        _ = SaveSelectedGitHubProxyAsync(value);
    }

    private async Task SaveSelectedGitHubProxyAsync(GitHubProxyEndpoint endpoint)
    {
        try
        {
            await _gitHubProxyService.SetSelectedEndpointAsync(endpoint.Id);
            GitHubProxyStatus = string.Equals(endpoint.Id, GitHubProxyService.DirectEndpointId, StringComparison.OrdinalIgnoreCase)
                ? "当前 GitHub 访问方式: 直连"
                : $"当前 GitHub 访问方式: {endpoint.Name}";
        }
        catch (Exception ex)
        {
            GitHubProxyStatus = $"GitHub 代理切换失败: {ex.Message}";
            _notifications.Error(ex, "GitHub 代理切换失败");
        }
    }

    private void LoadGitHubProxyEndpoints(string? preferredId = null)
    {
        _isLoadingProxySelection = true;
        try
        {
            GitHubProxyEndpoints.Clear();
            foreach (var endpoint in _gitHubProxyService.GetEndpoints())
            {
                GitHubProxyEndpoints.Add(endpoint);
            }

            var selected = string.IsNullOrWhiteSpace(preferredId)
                ? _gitHubProxyService.GetSelectedEndpoint()
                : GitHubProxyEndpoints.FirstOrDefault(endpoint => string.Equals(endpoint.Id, preferredId, StringComparison.OrdinalIgnoreCase));
            SelectedGitHubProxyEndpoint = GitHubProxyEndpoints.FirstOrDefault(endpoint => string.Equals(endpoint.Id, selected?.Id, StringComparison.OrdinalIgnoreCase))
                                          ?? GitHubProxyEndpoints.FirstOrDefault();
            GitHubProxyStatus = SelectedGitHubProxyEndpoint is null
                ? "未配置 GitHub 代理。"
                : string.Equals(SelectedGitHubProxyEndpoint.Id, GitHubProxyService.DirectEndpointId, StringComparison.OrdinalIgnoreCase)
                    ? "当前 GitHub 访问方式: 直连"
                    : $"当前 GitHub 访问方式: {SelectedGitHubProxyEndpoint.Name}";
        }
        finally
        {
            _isLoadingProxySelection = false;
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

    private async Task LoadBackendPreferenceAsync()
    {
        try
        {
            var pref = await _backendPreferenceService.GetAsync();
            SelectedBackendModeIndex = pref.BackendMode switch
            {
                BackendMode.Web => 1,
                _ => 0
            };
            UpdateBackendModeDescription();
        }
        catch
        {
            SelectedBackendModeIndex = 0;
        }
    }

    partial void OnSelectedBackendModeIndexChanged(int value)
    {
        _ = SaveBackendPreferenceAsync(value);
    }

    private async Task SaveBackendPreferenceAsync(int index)
    {
        try
        {
            var mode = index == 1 ? BackendMode.Web : BackendMode.Native;
            await _backendPreferenceService.SetAsync(mode);
            UpdateBackendModeDescription();
            _notifications.Info($"后台模式已切换为 {(mode == BackendMode.Web ? "Web 后台" : "原生后台")}，下次启动生效。");
        }
        catch
        {
        }
    }

    private void UpdateBackendModeDescription()
    {
        BackendModeDescription = SelectedBackendModeIndex == 1
            ? "当前: Web 后台 - 使用 WebView2 加载网页界面，下次启动生效。"
            : "当前: 原生后台 - 使用 Avalonia 原生界面，下次启动生效。";
    }

    private async Task<GitHubUser?> LoadProfileAsync(string login, CancellationToken cancellationToken)
    {
        try
        {
            return await _gitHubProxyService.GetFromJsonAsync<GitHubUser>(
                $"https://api.github.com/users/{Uri.EscapeDataString(login)}",
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<Bitmap?> LoadAvatarAsync(string avatarUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            return null;
        }

        try
        {
            var bytes = await _gitHubProxyService.GetByteArrayAsync(avatarUrl, cancellationToken);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
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

    protected override void Dispose(bool disposing)
    {
        if (!disposing || IsDisposed)
        {
            return;
        }

        _modelDownloadCts?.Cancel();
        _modelDownloadCts?.Dispose();
        _contributorsCts?.Cancel();
        _contributorsCts?.Dispose();

        base.Dispose(disposing);
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
    private readonly ISystemService _systemService;

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

    public ContributorViewModel(ISystemService systemService)
    {
        _systemService = systemService;
    }

    [RelayCommand]
    private void OpenProfile()
    {
        if (string.IsNullOrWhiteSpace(HtmlUrl))
        {
            return;
        }

        _systemService.OpenUrl(HtmlUrl);
    }
}
