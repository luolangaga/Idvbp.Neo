using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

public partial class SurPickItem : ObservableObject
{
    [ObservableProperty]
    private string _playerName = string.Empty;

    [ObservableProperty]
    private string? _previewImage;

    [ObservableProperty]
    private string _selectedChara = string.Empty;

    [ObservableProperty]
    private bool _isHighlighted;
}

public partial class HunPickItem : ObservableObject
{
    [ObservableProperty]
    private string? _previewImage;

    [ObservableProperty]
    private string _selectedChara = string.Empty;

    [ObservableProperty]
    private bool _isHighlighted;
}

public partial class GlobalBanRecordItem : ObservableObject
{
    [ObservableProperty]
    private string _recordedChara = string.Empty;
}

public partial class PickPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<SurPickItem> _surPickList = [];

    [ObservableProperty]
    private HunPickItem _hunPickVm = new();

    [ObservableProperty]
    private bool _surPickingBorder1;

    [ObservableProperty]
    private bool _surPickingBorder2;

    [ObservableProperty]
    private bool _surPickingBorder3;

    [ObservableProperty]
    private bool _surPickingBorder4;

    [ObservableProperty]
    private bool _hunPickingBorder;

    [ObservableProperty]
    private bool _isSingleControlEnabled;

    [ObservableProperty]
    private bool _isGlobalBanAutoRecord;

    [ObservableProperty]
    private string _mainTeamName = "主队";

    [ObservableProperty]
    private string _awayTeamName = "客队";

    [ObservableProperty]
    private ObservableCollection<GlobalBanRecordItem> _homeSurGlobalBanRecords = [];

    [ObservableProperty]
    private ObservableCollection<GlobalBanRecordItem> _homeHunGlobalBanRecords = [];

    [ObservableProperty]
    private ObservableCollection<GlobalBanRecordItem> _awaySurGlobalBanRecords = [];

    [ObservableProperty]
    private ObservableCollection<GlobalBanRecordItem> _awayHunGlobalBanRecords = [];
}
