using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Server.Services;
using Idvbp.Neo.ViewModels.Pages;
using Idvbp.Neo.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 设置页面视图模型。
/// </summary>
public partial class SettingPageViewModel : ViewModelBase
{
    private readonly IOfficialCharacterModelService _officialCharacterModelService;
    private readonly IServiceProvider _serviceProvider;
    private CancellationTokenSource? _modelDownloadCts;

    [ObservableProperty]
    private ObservableCollection<object> _languageList = new();

    [ObservableProperty]
    private object? _selectedLanguage;

    [ObservableProperty]
    private string _appVersion = "1.0.0";

    [ObservableProperty]
    private ObservableCollection<object> _mirrorList = new();

    [ObservableProperty]
    private object? _mirror;

    [ObservableProperty]
    private bool _isFindPreRelease;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadProgressText = "";

    [ObservableProperty]
    private string _mbPerSecondSpeed = "";

    [ObservableProperty]
    private bool _isModelDownloading;

    [ObservableProperty]
    private double _modelDownloadProgress;

    [ObservableProperty]
    private string _modelDownloadProgressText = "";

    [ObservableProperty]
    private string _modelDownloadStageText = "";

    /// <summary>
    /// 初始化设置页面视图模型。
    /// </summary>
    public SettingPageViewModel(IOfficialCharacterModelService officialCharacterModelService, IServiceProvider serviceProvider)
    {
        _officialCharacterModelService = officialCharacterModelService;
        _serviceProvider = serviceProvider;
        ModelDownloadProgressText = "未开始";
        ModelDownloadStageText = "官方模型会下载到 wwwroot/official-models";
    }

    /// <summary>
    /// 确保官方模型已下载命令。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEnsureOfficialModels))]
    private async Task EnsureOfficialModelsAsync()
    {
        _modelDownloadCts?.Cancel();
        _modelDownloadCts?.Dispose();
        _modelDownloadCts = new CancellationTokenSource();
        IsModelDownloading = true;
        ModelDownloadProgress = 0;
        ModelDownloadProgressText = "准备解析远程模型列表";
        ModelDownloadStageText = "正在连接官方资源列表...";
        EnsureOfficialModelsCommand.NotifyCanExecuteChanged();
        CancelOfficialModelDownloadCommand.NotifyCanExecuteChanged();

        var progress = new Progress<OfficialModelDownloadProgress>(item =>
        {
            ModelDownloadProgress = item.Total <= 0 ? 0 : item.Current * 100d / item.Total;
            ModelDownloadProgressText = $"{item.Current}/{item.Total}";
            ModelDownloadStageText = item.Status switch
            {
                "downloaded" => $"已下载: {item.ModelName}",
                "cached" => $"已存在: {item.ModelName}",
                "failed" => $"失败: {item.ModelName} - {item.Error}",
                _ => $"正在补齐: {item.ModelName}"
            };
        });

        try
        {
            var summary = await _officialCharacterModelService.EnsureAllModelsAsync(progress, _modelDownloadCts.Token);
            ModelDownloadProgress = 100;
            ModelDownloadProgressText = $"{summary.Total}/{summary.Total}";
            ModelDownloadStageText = $"补齐完成：新下载 {summary.Downloaded}，已存在 {summary.Cached}，失败 {summary.Failed}";
        }
        catch (OperationCanceledException)
        {
            ModelDownloadStageText = "已取消官方模型补齐";
        }
        catch (System.Exception ex)
        {
            ModelDownloadStageText = $"官方模型补齐失败：{ex.Message}";
        }
        finally
        {
            IsModelDownloading = false;
            EnsureOfficialModelsCommand.NotifyCanExecuteChanged();
            CancelOfficialModelDownloadCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// 是否可以执行确保官方模型命令。
    /// </summary>
    private bool CanEnsureOfficialModels() => !IsModelDownloading;

    /// <summary>
    /// 取消官方模型下载命令。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelOfficialModelDownload))]
    private void CancelOfficialModelDownload()
    {
        _modelDownloadCts?.Cancel();
    }

    /// <summary>
    /// 是否可以取消官方模型下载。
    /// </summary>
    private bool CanCancelOfficialModelDownload() => IsModelDownloading;

    partial void OnIsModelDownloadingChanged(bool value)
    {
        EnsureOfficialModelsCommand.NotifyCanExecuteChanged();
        CancelOfficialModelDownloadCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 打开日志查看器窗口。
    /// </summary>
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
}
