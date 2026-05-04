using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

/// <summary>
/// 地图选择模型，包含已禁用地图、选定地图与每方禁用槽位数。
/// </summary>
public partial class MapSelection : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<MapBanEntry> _bannedMaps = [];

    [ObservableProperty]
    private MapInfo? _pickedMap;

    [ObservableProperty]
    private int _banSlotsPerSide;
}

/// <summary>
/// 地图禁用条目模型。
/// </summary>
public partial class MapBanEntry : ObservableObject
{
    [ObservableProperty]
    private string _mapId = string.Empty;

    [ObservableProperty]
    private int _order;
}
