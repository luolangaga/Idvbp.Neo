using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Client;
using Idvbp.Neo.Models;
using Idvbp.Neo.Models.Enums;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Server.Resources;
using Idvbp.Neo.Service;

namespace Idvbp.Neo.ViewModels.Pages;

public sealed class MapOptionItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public bool IsBanned { get; init; }
    public bool IsPicked { get; init; }
    public bool CanBeBanned => !IsBanned && !IsPicked;
    public bool CanBePicked => !IsPicked && !IsBanned;
    public override string ToString() => Name;
}

/// <summary>
/// Map ban/pick page view model.
/// </summary>
public partial class MapBpPageViewModel : ViewModelBase
{
    private readonly BpApiClient _apiClient;
    private readonly BpRoomWorkspace _workspace;
    private bool _hasLoadedCatalog;
    private MapResourceItem[] _catalog = [];

    public MapBpPageViewModel(BpApiClient apiClient, BpRoomWorkspace workspace)
    {
        _apiClient = apiClient;
        _workspace = workspace;
        _workspace.ActiveRoomChanged += ApplyRoom;
        _workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(BpRoomWorkspace.IsSwitchingRoom) or nameof(BpRoomWorkspace.IsBusy) or nameof(BpRoomWorkspace.StatusMessage))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(StatusMessage));
            }
        };

        _ = InitializeAsync();
    }

    public bool IsBusy => _workspace.IsBusy || _workspace.IsSwitchingRoom;

    public string StatusMessage => _workspace.StatusMessage;

    public bool HasSelectedRoom => _workspace.SelectedRoom is not null;

    public string CurrentRoomTitle => _workspace.CurrentRoomTitle;

    [ObservableProperty]
    private bool _isBreathing = true;

    [ObservableProperty]
    private bool _isCampVisible = true;

    [ObservableProperty]
    private TeamSelectInfo? _pickMapTeam;

    [ObservableProperty]
    private TeamSelectInfo? _banMapTeam;

    [ObservableProperty]
    private ObservableCollection<MapOptionItem> _mapOptions = [];

    [ObservableProperty]
    private MapOptionItem? _pickedMap;

    [ObservableProperty]
    private ObservableCollection<MapOptionItem> _bannedMapList = [];

    [ObservableProperty]
    private ObservableCollection<TeamSelectInfo> _mapSelectTeamsList = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await EnsureCatalogAsync();
        ApplyRoom(_workspace.SelectedRoom);
    }

    [RelayCommand]
    private async Task PickMapAsync()
    {
        if (PickedMap is null)
        {
            return;
        }

        var room = await _workspace.UpdateMapAsync(new UpdateMapRequest
        {
            MapId = PickedMap.Id,
            MapName = PickedMap.Name,
            ImageUrl = PickedMap.ImageUrl,
            NextPhase = BpPhase.SideBans
        });

        if (room is not null)
        {
            ApplyRoom(room);
        }
    }

    [RelayCommand]
    private async Task BanMapAsync(MapOptionItem? map)
    {
        if (map is null || !map.CanBeBanned)
        {
            return;
        }

        var room = await _workspace.AddMapBanAsync(new AddMapBanRequest
        {
            MapId = map.Id
        });

        if (room is not null)
        {
            ApplyRoom(room);
        }
    }

    [RelayCommand]
    private void ResetLocalSelection()
    {
        PickedMap = null;
        ApplyRoom(_workspace.SelectedRoom);
    }

    private async Task InitializeAsync()
    {
        await EnsureCatalogAsync();
        ApplyRoom(_workspace.SelectedRoom);
    }

    private async Task EnsureCatalogAsync()
    {
        if (_hasLoadedCatalog)
        {
            return;
        }

        _catalog = (await _apiClient.GetMapsAsync()).ToArray();
        _hasLoadedCatalog = true;
    }

    private void ApplyRoom(BpRoom? room)
    {
        OnPropertyChanged(nameof(HasSelectedRoom));
        OnPropertyChanged(nameof(CurrentRoomTitle));

        if (room is null)
        {
            MapOptions = [];
            BannedMapList = [];
            MapSelectTeamsList = [];
            PickedMap = null;
            return;
        }

        MapSelectTeamsList = new ObservableCollection<TeamSelectInfo>
        {
            new() { TeamType = room.TeamA.CurrentSide.ToString(), TeamName = room.TeamA.Name },
            new() { TeamType = room.TeamB.CurrentSide.ToString(), TeamName = room.TeamB.Name }
        };
        PickMapTeam ??= MapSelectTeamsList.FirstOrDefault();
        BanMapTeam ??= MapSelectTeamsList.FirstOrDefault();

        var bannedIds = room.MapSelection.BannedMaps.Select(x => x.MapId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pickedId = room.MapSelection.PickedMap?.Id;
        var options = _catalog
            .Select(map => ToOption(map, bannedIds.Contains(map.Id), string.Equals(map.Id, pickedId, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        MapOptions = new ObservableCollection<MapOptionItem>(options);
        BannedMapList = new ObservableCollection<MapOptionItem>(options.Where(x => x.IsBanned));
        PickedMap = options.FirstOrDefault(x => x.IsPicked)
                    ?? options.FirstOrDefault(x => string.Equals(x.Id, pickedId, StringComparison.OrdinalIgnoreCase));
    }

    private MapOptionItem ToOption(MapResourceItem resource, bool isBanned, bool isPicked)
    {
        var name = resource.Names.TryGetValue("zh-CN", out var chineseName) && !string.IsNullOrWhiteSpace(chineseName)
            ? chineseName!
            : resource.Names.Values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? resource.Id;
        var image = resource.Images.FirstOrDefault(x => string.Equals(x.Variant, "singleColor", StringComparison.OrdinalIgnoreCase))
                    ?? resource.Images.FirstOrDefault(x => x.IsPrimary)
                    ?? resource.Images.FirstOrDefault();

        return new MapOptionItem
        {
            Id = resource.Id,
            Name = name,
            ImageUrl = _apiClient.ToAbsoluteUrl(image?.Url),
            IsBanned = isBanned,
            IsPicked = isPicked
        };
    }
}
