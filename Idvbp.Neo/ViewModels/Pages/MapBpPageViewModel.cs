using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Models;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 地图 BP 页面视图模型。
/// </summary>
public partial class MapBpPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isBreathing = true;

    [ObservableProperty]
    private bool _isCampVisible = true;

    [ObservableProperty]
    private TeamSelectInfo? _pickMapTeam;

    [ObservableProperty]
    private TeamSelectInfo? _banMapTeam;

    [ObservableProperty]
    private ObservableCollection<MapInfo> _pickedMapSelections = [];

    [ObservableProperty]
    private MapInfo? _pickedMap;

    [ObservableProperty]
    private ObservableCollection<MapInfo> _bannedMapList = [];

    [ObservableProperty]
    private ObservableCollection<TeamSelectInfo> _mapSelectTeamsList = [];
}
