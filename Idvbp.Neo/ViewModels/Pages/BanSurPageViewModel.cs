using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

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

public partial class BanSurPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<BanSlotItem> _currentBanList = [];

    [ObservableProperty]
    private ObservableCollection<BanSlotItem> _globalBanList = [];
}
