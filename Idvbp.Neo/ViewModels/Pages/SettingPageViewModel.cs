using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

public partial class SettingPageViewModel : ViewModelBase
{
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
}
