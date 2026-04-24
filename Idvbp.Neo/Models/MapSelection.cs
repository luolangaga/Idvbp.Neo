using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

public partial class MapSelection : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<MapBanEntry> _bannedMaps = [];

    [ObservableProperty]
    private MapInfo? _pickedMap;

    [ObservableProperty]
    private int _banSlotsPerSide;
}

public partial class MapBanEntry : ObservableObject
{
    [ObservableProperty]
    private string _mapId = string.Empty;

    [ObservableProperty]
    private int _order;
}
