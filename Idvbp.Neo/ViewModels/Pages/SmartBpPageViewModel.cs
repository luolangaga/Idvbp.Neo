using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

public partial class SmartBpPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<object> _captureMethodList = new();

    [ObservableProperty]
    private object? _selectedCaptureMethod;

    [ObservableProperty]
    private ObservableCollection<string> _activeWindows = new();

    [ObservableProperty]
    private string? _selectedWindow;

    [ObservableProperty]
    private string _regionConfigPath = "未配置";

    [ObservableProperty]
    private string _regionConfigAspectRatioText = "-";

    [ObservableProperty]
    private string _captureAspectRatioText = "-";

    [ObservableProperty]
    private string _regionAspectStatusText = "正常";

    [ObservableProperty]
    private bool _regionAspectIsMismatch;

    [ObservableProperty]
    private string _regionAspectHintText = "";

    [ObservableProperty]
    private ObservableCollection<object> _ocrModelList = new();

    [ObservableProperty]
    private object? _selectedOcrModel;

    [ObservableProperty]
    private bool _isModelDownloading;

    [ObservableProperty]
    private bool _hasPreciseDownloadProgress;

    [ObservableProperty]
    private double _modelDownloadProgress;

    [ObservableProperty]
    private string _modelDownloadProgressText = "";

    [ObservableProperty]
    private string _modelDownloadStageText = "";

    [ObservableProperty]
    private string _currentOcrModelDisplayName = "未选择";

    [ObservableProperty]
    private bool _showDownloadModelButton = true;

    [ObservableProperty]
    private bool _showDeleteModelButton;
}
