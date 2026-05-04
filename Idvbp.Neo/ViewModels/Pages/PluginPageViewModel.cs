using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 插件页面视图模型。
/// </summary>
public partial class PluginPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isRestartNeeded;

    [ObservableProperty]
    private ObservableCollection<object> _pluginsCollection = new();

    [ObservableProperty]
    private ObservableCollection<object> _marketPluginsCollection = new();

    [ObservableProperty]
    private object? _selectedMarketPlugin;

    [ObservableProperty]
    private bool _isMarketLoading;

    [ObservableProperty]
    private bool _hasMarketError;

    [ObservableProperty]
    private string _marketErrorMessage = "";

    [ObservableProperty]
    private bool _isDownloadQueueOpen;

    [ObservableProperty]
    private bool _isMarketPluginSelected;

    [ObservableProperty]
    private ObservableCollection<object> _pluginDownloadQueue = new();

    [ObservableProperty]
    private bool _hasPluginDownloadQueueItems;
}
