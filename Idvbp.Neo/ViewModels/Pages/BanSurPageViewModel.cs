using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 求生者禁用槽位项。
/// </summary>
public partial class BanSlotItem : ObservableObject
{
    [ObservableProperty]
    private string _selectedChara = string.Empty;

    [ObservableProperty]
    private string? _previewImage;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isHighlighted;

    [ObservableProperty]
    private int _index;
}

/// <summary>
/// 求生者禁用页面视图模型。
/// </summary>
public partial class BanSurPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<BanSlotItem> _currentBanList = [];

    [ObservableProperty]
    private ObservableCollection<BanSlotItem> _globalBanList = [];
}
