using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Client;
using Idvbp.Neo.Models;
using Idvbp.Neo.Models.Enums;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Server.Resources;
using Idvbp.Neo.Service;
using Idvbp.Neo.Services;
using ToolGood.Words.Pinyin;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 角色选项项。
/// </summary>
public sealed class CharacterOptionItem
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string? Abbrev { get; init; }

    public string? FullSpell { get; init; }

    public string? PreviewImageUrl { get; init; }

    public string? PreviewImageRelativePath { get; init; }

    public override string ToString() => DisplayName;
}

/// <summary>
/// 玩家选项项。
/// </summary>
public sealed class PlayerOptionItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string TeamId { get; init; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>
/// 全局禁用记录项。
/// </summary>
public partial class GlobalBanRecordItem : ObservableObject
{
    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private string _characterId = string.Empty;

    [ObservableProperty]
    private string _characterName = string.Empty;
}

/// <summary>
/// 选人槽位项。
/// </summary>
public partial class PickSlotItem : ObservableObject
{
    private bool _suppressSelectionCallbacks;

    public Func<PickSlotItem, Task>? SubmitSelectionAsync { get; set; }

    public Func<PickSlotItem, IReadOnlyList<CharacterOptionItem>>? SearchCharacters { get; set; }

    [ObservableProperty]
    private string _slot = string.Empty;

    [ObservableProperty]
    private string _slotTitle = string.Empty;

    [ObservableProperty]
    private string _roleTitle = string.Empty;

    [ObservableProperty]
    private int _seatNumber;

    [ObservableProperty]
    private string _playerId = string.Empty;

    [ObservableProperty]
    private string _playerName = string.Empty;

    [ObservableProperty]
    private string _teamId = string.Empty;

    [ObservableProperty]
    private string _teamName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PlayerOptionItem> _availablePlayers = [];

    [ObservableProperty]
    private PlayerOptionItem? _selectedPlayer;

    [ObservableProperty]
    private IReadOnlyList<CharacterOptionItem> _availableCharacters = Array.Empty<CharacterOptionItem>();

    [ObservableProperty]
    private CharacterOptionItem? _selectedCharacter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CharacterOptionItem> _filteredCharacters = [];

    [ObservableProperty]
    private CharacterOptionItem? _pendingCharacter;

    [ObservableProperty]
    private Bitmap? _selectedCharacterPreviewImage;

    [ObservableProperty]
    private bool _isSubmitting;

    [ObservableProperty]
    private string _submitStateText = string.Empty;

    /// <summary>
    /// 是否可以确认选择。
    /// </summary>
    public bool CanConfirmSelection
        => PendingCharacter is not null
           && !string.Equals(PendingCharacter.Id, SelectedCharacter?.Id, StringComparison.OrdinalIgnoreCase)
           && !IsSubmitting;

    /// <summary>
    /// 设置选中的角色。
    /// </summary>
    public void SetSelection(CharacterOptionItem? character, bool synchronizeSearchText)
    {
        _suppressSelectionCallbacks = true;
        SelectedCharacter = character;
        PendingCharacter = character;
        if (synchronizeSearchText)
        {
            SearchText = character?.DisplayName ?? string.Empty;
        }

        _suppressSelectionCallbacks = false;
        RebuildFilteredCharacters();
    }

    /// <summary>
    /// 设置可选玩家列表。
    /// </summary>
    public void SetPlayers(IEnumerable<PlayerOptionItem> players, string? selectedPlayerId, string? selectedPlayerName)
    {
        var playerOptions = players.ToArray();
        AvailablePlayers = new ObservableCollection<PlayerOptionItem>(playerOptions);
        SelectedPlayer = playerOptions.FirstOrDefault(x => string.Equals(x.Id, selectedPlayerId, StringComparison.OrdinalIgnoreCase))
                         ?? playerOptions.FirstOrDefault(x => string.Equals(x.Name, selectedPlayerName, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSelectedCharacterChanged(CharacterOptionItem? value)
    {
        if (_suppressSelectionCallbacks)
        {
            return;
        }

        if (value is not null)
        {
            _suppressSelectionCallbacks = true;
            SearchText = value.DisplayName;
            PendingCharacter = value;
            _suppressSelectionCallbacks = false;
        }

        RebuildFilteredCharacters();
    }

    partial void OnAvailableCharactersChanged(IReadOnlyList<CharacterOptionItem> value)
    {
        if (_suppressSelectionCallbacks)
        {
            return;
        }

        RebuildFilteredCharacters();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_suppressSelectionCallbacks)
        {
            return;
        }

        RebuildFilteredCharacters();
    }

    partial void OnPendingCharacterChanged(CharacterOptionItem? value)
    {
        if (_suppressSelectionCallbacks)
        {
            return;
        }

    }

    partial void OnSelectedPlayerChanged(PlayerOptionItem? value)
    {
        if (value is null)
        {
            return;
        }

        PlayerId = value.Id;
        PlayerName = value.Name;
        TeamId = value.TeamId;
    }

    partial void OnIsSubmittingChanged(bool value)
        => ConfirmSelectionCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// 确认选择命令。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConfirmSelection))]
    public async Task ConfirmSelectionAsync()
    {
        if (!CanConfirmSelection || PendingCharacter is null || SubmitSelectionAsync is null)
        {
            return;
        }

        await SubmitSelectionAsync(this);
    }

    /// <summary>
    /// 确认当前待选角色。
    /// </summary>
    public Task ConfirmPendingSelectionAsync()
    {
        if (PendingCharacter is null && FilteredCharacters.Count > 0)
        {
            PendingCharacter = FilteredCharacters[0];
        }

        return ConfirmSelectionAsync();
    }

    /// <summary>
    /// 重建过滤后的角色列表。
    /// </summary>
    private void RebuildFilteredCharacters()
    {
        var matches = SearchCharacters?.Invoke(this)
                      ?? SearchCharactersInternal(AvailableCharacters, SearchText);

        FilteredCharacters = new ObservableCollection<CharacterOptionItem>(matches);

        CharacterOptionItem? nextPendingCharacter = null;
        if (PendingCharacter is not null)
        {
            nextPendingCharacter = matches.FirstOrDefault(x => string.Equals(x.Id, PendingCharacter.Id, StringComparison.OrdinalIgnoreCase));
        }

        nextPendingCharacter ??= SelectedCharacter is null
            ? matches.FirstOrDefault()
            : matches.FirstOrDefault(x => string.Equals(x.Id, SelectedCharacter.Id, StringComparison.OrdinalIgnoreCase)) ?? matches.FirstOrDefault();

        _suppressSelectionCallbacks = true;
        PendingCharacter = nextPendingCharacter;
        _suppressSelectionCallbacks = false;
        ConfirmSelectionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 内部搜索角色。
    /// </summary>
    private static IReadOnlyList<CharacterOptionItem> SearchCharactersInternal(IReadOnlyList<CharacterOptionItem> availableCharacters, string searchText)
    {
        var normalizedQuery = NormalizeSearch(searchText);
        return availableCharacters
            .Select(character => new { Character = character, Rank = GetMatchRank(character, normalizedQuery) })
            .Where(x => x.Rank < int.MaxValue)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Character.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Character)
            .ToArray();
    }

    /// <summary>
    /// 获取角色匹配排名。
    /// </summary>
    private static int GetMatchRank(CharacterOptionItem character, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var displayName = NormalizeSearch(character.DisplayName);
        var abbrev = NormalizeSearch(character.Abbrev);
        var fullSpell = NormalizeSearch(character.FullSpell);
        var id = NormalizeSearch(character.Id);

        if (abbrev == query || fullSpell == query || displayName == query || id == query)
        {
            return 0;
        }

        if (abbrev.StartsWith(query, StringComparison.Ordinal))
        {
            return 1;
        }

        if (fullSpell.StartsWith(query, StringComparison.Ordinal))
        {
            return 2;
        }

        if (displayName.StartsWith(query, StringComparison.Ordinal))
        {
            return 3;
        }

        if (id.StartsWith(query, StringComparison.Ordinal))
        {
            return 4;
        }

        if (displayName.Contains(query, StringComparison.Ordinal))
        {
            return 5;
        }

        if (abbrev.Contains(query, StringComparison.Ordinal))
        {
            return 6;
        }

        if (fullSpell.Contains(query, StringComparison.Ordinal))
        {
            return 7;
        }

        return id.Contains(query, StringComparison.Ordinal) ? 8 : int.MaxValue;
    }

    /// <summary>
    /// 规范化搜索文本。
    /// </summary>
    private static string NormalizeSearch(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}

/// <summary>
/// 选人页面视图模型。
/// </summary>
public partial class PickPageViewModel : ViewModelBase
{
    private readonly BpApiClient _apiClient;
    private readonly RoomRealtimeClient? _realtimeClient = null;
    private readonly BpRoomWorkspace _workspace;
    private readonly AppNotificationService _notifications;
    private readonly PinyinMatch _survivorPinyinMatch = new();
    private readonly PinyinMatch _hunterPinyinMatch = new();
    private readonly Dictionary<string, CharacterOptionItem> _characterLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap?> _previewImageCache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CharacterOptionItem> _survivorCharacters = Array.Empty<CharacterOptionItem>();
    private IReadOnlyList<CharacterOptionItem> _hunterCharacters = Array.Empty<CharacterOptionItem>();
    private bool _hasLoadedCharacterCatalog;
    private bool _isRefreshing;
    private bool _suppressSelectedRoomLoad;
    private string? _subscribedRoomId;

    private static readonly string[] RealtimeEventTypes =
    [
        RoomEventNames.RoomSnapshot,
        RoomEventNames.RoomInfoUpdated,
        RoomEventNames.MatchCreated,
        RoomEventNames.MapUpdated,
        RoomEventNames.RoleSelected,
        RoomEventNames.BanUpdated,
        RoomEventNames.GlobalBanUpdated,
        RoomEventNames.PhaseUpdated
    ];
    /// <summary>
    /// 初始化选人页面视图模型。
    /// </summary>
    public PickPageViewModel(BpApiClient apiClient, BpRoomWorkspace workspace, AppNotificationService notifications)
    {
        _apiClient = apiClient;
        _workspace = workspace;
        _notifications = notifications;
        HunPickVm.SubmitSelectionAsync = SubmitSelectedSlotAsync;
        _workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(BpRoomWorkspace.Rooms))
            {
                Rooms = _workspace.Rooms;
            }

            if (args.PropertyName is nameof(BpRoomWorkspace.SelectedRoom))
            {
                SyncSelectedRoomFromWorkspace();
            }
        };
        _workspace.ActiveRoomChanged += _ => SyncSelectedRoomFromWorkspace();

        _ = InitializeAsync();
    }

    [ObservableProperty]
    private ObservableCollection<BpRoom> _rooms = [];

    [ObservableProperty]
    private BpRoom? _selectedRoom;

    [ObservableProperty]
    private string _selectedRoomDisplayName = "未选择房间";

    [ObservableProperty]
    private string _roomPhaseText = "阶段: 未知";

    [ObservableProperty]
    private string _roomRoundText = "回合: -";

    [ObservableProperty]
    private string _survivorTeamName = "求生者方";

    [ObservableProperty]
    private string _hunterTeamName = "监管者方";

    [ObservableProperty]
    private string _statusMessage = "正在加载角色与房间数据...";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private ObservableCollection<PickSlotItem> _surPickList = [];

    [ObservableProperty]
    private PickSlotItem _hunPickVm = new();

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

    /// <summary>
    /// 是否有房间。
    /// </summary>
    public bool HasRooms => Rooms.Count > 0;

    partial void OnSelectedRoomChanged(BpRoom? value)
    {
        if (_suppressSelectedRoomLoad)
        {
            return;
        }

        if (value is null)
        {
            return;
        }

        _ = _workspace.SwitchRoomAsync(value.RoomId);
        ApplyRoom(value);
    }

    /// <summary>
    /// 刷新数据命令。
    /// </summary>
    [RelayCommand]
    private async Task RefreshDataAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        IsBusy = true;

        try
        {
            StatusMessage = "正在同步房间和角色数据...";

            if (!_hasLoadedCharacterCatalog)
            {
                await LoadCharacterCatalogAsync();
            }

            await _workspace.RefreshRoomsAsync();
            Rooms = _workspace.Rooms;
            OnPropertyChanged(nameof(HasRooms));

            if (Rooms.Count == 0)
            {
                SelectedRoom = null;
                ApplyEmptyRoomState();
                StatusMessage = "当前还没有房间，请先创建房间后再进行选角。";
                _notifications.Warning(StatusMessage);
                return;
            }

            SyncSelectedRoomFromWorkspace();
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
            _notifications.Error(ex, "加载选角数据失败");
        }
        finally
        {
            IsBusy = false;
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// 提交槽位选择命令。
    /// </summary>
    [RelayCommand]
    private Task SubmitSlotAsync(PickSlotItem? slot)
        => slot is null ? Task.CompletedTask : SubmitSlotCoreAsync(slot, CancellationToken.None);

    /// <summary>
    /// 初始化视图模型。
    /// </summary>
    private async Task InitializeAsync()
    {
        await RefreshDataAsync();
    }

    /// <summary>
    /// 加载角色目录。
    /// </summary>
    private async Task LoadCharacterCatalogAsync()
    {
        var characters = await _apiClient.GetCharactersAsync();

        _characterLookup.Clear();
        var normalizedCharacters = characters
            .Select(CreateCharacterOption)
            .Where(x => x is not null)
            .Cast<CharacterOptionItem>()
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var character in normalizedCharacters)
        {
            _characterLookup[character.Id] = character;
        }

        _survivorCharacters = normalizedCharacters.Where(x => string.Equals(x.Role, "survivor", StringComparison.OrdinalIgnoreCase)).ToArray();
        _hunterCharacters = normalizedCharacters.Where(x => string.Equals(x.Role, "hunter", StringComparison.OrdinalIgnoreCase)).ToArray();
        _survivorPinyinMatch.SetKeywords(_survivorCharacters.Select(x => x.DisplayName).ToList());
        _hunterPinyinMatch.SetKeywords(_hunterCharacters.Select(x => x.DisplayName).ToList());
        _hasLoadedCharacterCatalog = true;
    }

    /// <summary>
    /// 从资源创建角色选项。
    /// </summary>
    private CharacterOptionItem? CreateCharacterOption(CharacterResourceItem resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Id))
        {
            return null;
        }

        var displayName = resource.Names.TryGetValue("zh-CN", out var chineseName) && !string.IsNullOrWhiteSpace(chineseName)
            ? chineseName!
            : resource.Names.Values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? resource.Id;

        var primaryImage = resource.Images.FirstOrDefault(x => string.Equals(x.Variant, "half", StringComparison.OrdinalIgnoreCase))
            ?? resource.Images.FirstOrDefault(x => x.IsPrimary)
            ?? resource.Images.FirstOrDefault();

        return new CharacterOptionItem
        {
            Id = resource.Id,
            DisplayName = displayName,
            Role = resource.Role,
            Abbrev = resource.Abbrev,
            FullSpell = resource.FullSpell,
            PreviewImageUrl = _apiClient.ToAbsoluteUrl(primaryImage?.Url),
            PreviewImageRelativePath = primaryImage?.RelativePath
        };
    }

    /// <summary>
    /// 切换实时房间订阅。
    /// </summary>
    private async Task SwitchRealtimeRoomAsync(string? roomId)
    {
        if (_realtimeClient is null)
        {
            await _workspace.SwitchRoomAsync(roomId);
            return;
        }

        if (string.IsNullOrWhiteSpace(roomId))
        {
            ApplyEmptyRoomState();
            return;
        }

        try
        {
            IsBusy = true;

            if (!string.IsNullOrWhiteSpace(_subscribedRoomId) && !string.Equals(_subscribedRoomId, roomId, StringComparison.OrdinalIgnoreCase))
            {
                await _realtimeClient.LeaveRoomAsync(_subscribedRoomId);
            }

            await _realtimeClient.SubscribeToRoomAsync(roomId, RealtimeEventTypes);
            _subscribedRoomId = roomId;
            await _realtimeClient.RequestRoomSnapshotAsync(roomId);
            StatusMessage = "已通过 SignalR 订阅房间并请求最新快照。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"连接房间实时通道失败: {ex.Message}";
            _notifications.Error(ex, "连接房间实时通道失败");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 应用房间状态到视图。
    /// </summary>
    private void ApplyRoom(BpRoom room)
    {
        SelectedRoomDisplayName = string.IsNullOrWhiteSpace(room.RoomName)
            ? room.RoomId
            : $"{room.RoomName} ({room.RoomId})";
        RoomPhaseText = $"阶段: {room.CurrentPhase}";
        RoomRoundText = $"回合: 第 {room.CurrentRound} 局";
        MainTeamName = string.IsNullOrWhiteSpace(room.TeamA.Name) ? "主队" : room.TeamA.Name;
        AwayTeamName = string.IsNullOrWhiteSpace(room.TeamB.Name) ? "客队" : room.TeamB.Name;

        var survivorTeam = room.TeamA.CurrentSide == GameSide.Survivor ? room.TeamA : room.TeamB;
        var hunterTeam = room.TeamA.CurrentSide == GameSide.Hunter ? room.TeamA : room.TeamB;

        SurvivorTeamName = string.IsNullOrWhiteSpace(survivorTeam.Name) ? "求生者方" : survivorTeam.Name;
        HunterTeamName = string.IsNullOrWhiteSpace(hunterTeam.Name) ? "监管者方" : hunterTeam.Name;

        EnsureSurvivorSlots();
        UpdateSlotFromRoom(SurPickList[0], "求生者 1", "求生者", 1, room.CharacterPicks.Survivor1, survivorTeam, _survivorCharacters);
        UpdateSlotFromRoom(SurPickList[1], "求生者 2", "求生者", 2, room.CharacterPicks.Survivor2, survivorTeam, _survivorCharacters);
        UpdateSlotFromRoom(SurPickList[2], "求生者 3", "求生者", 3, room.CharacterPicks.Survivor3, survivorTeam, _survivorCharacters);
        UpdateSlotFromRoom(SurPickList[3], "求生者 4", "求生者", 4, room.CharacterPicks.Survivor4, survivorTeam, _survivorCharacters);

        UpdateSlotFromRoom(HunPickVm, "监管者", "监管者", 1, room.CharacterPicks.Hunter, hunterTeam, _hunterCharacters);

        HomeSurGlobalBanRecords = BuildBanRecords(room.GlobalBans.SurvivorBans);
        HomeHunGlobalBanRecords = BuildBanRecords(room.GlobalBans.HunterBans);
        AwaySurGlobalBanRecords = BuildBanRecords(room.Bans.SurvivorBans);
        AwayHunGlobalBanRecords = BuildBanRecords(room.Bans.HunterBans);
    }

    /// <summary>
    /// 创建求生者槽位。
    /// </summary>
    private PickSlotItem CreateSurvivorSlot(string slot, int seatNumber, Player player, Team team)
    {
        var slotItem = CreateSlot(slot, $"求生者 {seatNumber}", "求生者", seatNumber, player, team, _survivorCharacters);
        slotItem.SubmitSelectionAsync = SubmitSelectedSlotAsync;
        slotItem.SearchCharacters = SearchCharactersForSlot;
        return slotItem;
    }

    /// <summary>
    /// 确保求生者槽位存在。
    /// </summary>
    private void EnsureSurvivorSlots()
    {
        if (SurPickList.Count == 4)
        {
            return;
        }

        SurPickList = new ObservableCollection<PickSlotItem>
        {
            new() { Slot = "Survivor1", SubmitSelectionAsync = SubmitSelectedSlotAsync, SearchCharacters = SearchCharactersForSlot },
            new() { Slot = "Survivor2", SubmitSelectionAsync = SubmitSelectedSlotAsync, SearchCharacters = SearchCharactersForSlot },
            new() { Slot = "Survivor3", SubmitSelectionAsync = SubmitSelectedSlotAsync, SearchCharacters = SearchCharactersForSlot },
            new() { Slot = "Survivor4", SubmitSelectionAsync = SubmitSelectedSlotAsync, SearchCharacters = SearchCharactersForSlot }
        };
    }

    /// <summary>
    /// 从房间状态更新槽位。
    /// </summary>
    private void UpdateSlotFromRoom(PickSlotItem slotItem, string slotTitle, string roleTitle, int seatNumber, Player player, Team team, IReadOnlyList<CharacterOptionItem> characters)
    {
        slotItem.Slot = seatNumber == 1 && string.Equals(roleTitle, "监管者", StringComparison.Ordinal)
            ? "Hunter"
            : slotItem.Slot;

        var teamId = !string.IsNullOrWhiteSpace(player.TeamId)
            ? player.TeamId
            : team.Id;
        var playerId = !string.IsNullOrWhiteSpace(player.Id)
            ? player.Id
            : BuildDefaultPlayerId(teamId, slotItem.Slot);
        var playerName = !string.IsNullOrWhiteSpace(player.Name)
            ? player.Name
            : slotTitle;

        slotItem.SlotTitle = slotTitle;
        slotItem.RoleTitle = roleTitle;
        slotItem.SeatNumber = seatNumber;
        slotItem.TeamId = teamId;
        slotItem.TeamName = team.Name;
        slotItem.PlayerId = playerId;
        slotItem.PlayerName = playerName;
        slotItem.SetPlayers(BuildPlayerOptions(team), playerId, playerName);
        slotItem.AvailableCharacters = characters;
        slotItem.SubmitSelectionAsync = SubmitSelectedSlotAsync;
        slotItem.SearchCharacters = SearchCharactersForSlot;
        slotItem.SubmitStateText = string.IsNullOrWhiteSpace(player.CharacterId) ? string.Empty : "已同步";
        slotItem.SetSelection(TryGetCharacter(player.CharacterId), synchronizeSearchText: true);
        _ = LoadSlotPreviewImageAsync(slotItem);
    }

    /// <summary>
    /// 创建监管者槽位。
    /// </summary>
    private PickSlotItem CreateHunterSlot(Player player, Team team)
    {
        var slotItem = CreateSlot("Hunter", "监管者", "监管者", 1, player, team, _hunterCharacters);
        slotItem.SubmitSelectionAsync = SubmitSelectedSlotAsync;
        slotItem.SearchCharacters = SearchCharactersForSlot;
        return slotItem;
    }

    /// <summary>
    /// 创建槽位。
    /// </summary>
    private PickSlotItem CreateSlot(string slot, string slotTitle, string roleTitle, int seatNumber, Player player, Team team, IReadOnlyList<CharacterOptionItem> characters)
    {
        var teamId = !string.IsNullOrWhiteSpace(player.TeamId)
            ? player.TeamId
            : team.Id;
        var playerId = !string.IsNullOrWhiteSpace(player.Id)
            ? player.Id
            : BuildDefaultPlayerId(teamId, slot);
        var playerName = !string.IsNullOrWhiteSpace(player.Name)
            ? player.Name
            : slotTitle;

        var slotItem = new PickSlotItem
        {
            Slot = slot,
            SlotTitle = slotTitle,
            RoleTitle = roleTitle,
            SeatNumber = seatNumber,
            TeamId = teamId,
            TeamName = team.Name,
            PlayerId = playerId,
            PlayerName = playerName,
            AvailablePlayers = new ObservableCollection<PlayerOptionItem>(BuildPlayerOptions(team)),
            AvailableCharacters = characters,
            SubmitSelectionAsync = SubmitSelectedSlotAsync,
            SearchCharacters = SearchCharactersForSlot,
            SubmitStateText = string.IsNullOrWhiteSpace(player.CharacterId) ? "未提交选角" : "已同步当前房间选角"
        };
        slotItem.SelectedPlayer = slotItem.AvailablePlayers.FirstOrDefault(x => string.Equals(x.Id, playerId, StringComparison.OrdinalIgnoreCase))
                                  ?? slotItem.AvailablePlayers.FirstOrDefault(x => string.Equals(x.Name, playerName, StringComparison.OrdinalIgnoreCase));
        slotItem.SetSelection(TryGetCharacter(player.CharacterId), synchronizeSearchText: true);
        return slotItem;
    }

    /// <summary>
    /// 构建玩家选项列表。
    /// </summary>
    private static IReadOnlyList<PlayerOptionItem> BuildPlayerOptions(Team team)
        => team.Members
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select((player, index) => new PlayerOptionItem
            {
                Id = string.IsNullOrWhiteSpace(player.Id) ? $"{team.Id}-member-{index + 1}" : player.Id,
                Name = player.Name.Trim(),
                TeamId = string.IsNullOrWhiteSpace(player.TeamId) ? team.Id : player.TeamId
            })
            .ToArray();

    /// <summary>
    /// 构建禁用记录列表。
    /// </summary>
    private ObservableCollection<GlobalBanRecordItem> BuildBanRecords(IEnumerable<PickBanEntry> bans)
        => new(bans
            .OrderBy(x => x.Order)
            .Select(x => new GlobalBanRecordItem
            {
                Order = x.Order,
                CharacterId = x.CharacterId,
                CharacterName = ResolveCharacterName(x.CharacterId)
            }));

    /// <summary>
    /// 为槽位搜索角色。
    /// </summary>
    private IReadOnlyList<CharacterOptionItem> SearchCharactersForSlot(PickSlotItem slot)
    {
        var availableCharacters = slot.AvailableCharacters;
        if (availableCharacters.Count == 0)
        {
            return Array.Empty<CharacterOptionItem>();
        }

        var query = slot.SearchText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return availableCharacters
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var pinyinMatch = string.Equals(slot.Slot, "Hunter", StringComparison.OrdinalIgnoreCase)
            ? _hunterPinyinMatch
            : _survivorPinyinMatch;

        var matchedNames = pinyinMatch.Find(query);
        var matchedItems = availableCharacters
            .Where(character => matchedNames.Contains(character.DisplayName, StringComparer.OrdinalIgnoreCase))
            .OrderBy(character => matchedNames.FindIndex(x => string.Equals(x, character.DisplayName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var character in availableCharacters
                     .Where(character =>
                         character.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || character.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (matchedItems.Any(x => string.Equals(x.Id, character.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            matchedItems.Add(character);
        }

        return matchedItems;
    }

    /// <summary>
    /// 尝试获取角色。
    /// </summary>
    private CharacterOptionItem? TryGetCharacter(string? characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return null;
        }

        return _characterLookup.GetValueOrDefault(characterId);
    }

    /// <summary>
    /// 解析角色名称。
    /// </summary>
    private string ResolveCharacterName(string? characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return "未记录";
        }

        return TryGetCharacter(characterId)?.DisplayName ?? characterId;
    }

    /// <summary>
    /// 提交选中槽位。
    /// </summary>
    private Task SubmitSelectedSlotAsync(PickSlotItem slot)
        => SubmitSlotCoreAsync(slot, CancellationToken.None);

    /// <summary>
    /// 提交槽位核心逻辑。
    /// </summary>
    private async Task SubmitSlotCoreAsync(PickSlotItem slot, CancellationToken cancellationToken)
    {
        if (SelectedRoom is null)
        {
            slot.SubmitStateText = "当前没有可用房间。";
            return;
        }

        var pendingCharacter = slot.PendingCharacter;
        if (pendingCharacter is null)
        {
            slot.SubmitStateText = "请选择角色后再提交。";
            return;
        }

        var teamId = string.IsNullOrWhiteSpace(slot.TeamId)
            ? BuildDefaultTeamId(slot)
            : slot.TeamId.Trim();
        var playerName = string.IsNullOrWhiteSpace(slot.PlayerName)
            ? slot.SlotTitle
            : slot.PlayerName.Trim();
        var playerId = string.IsNullOrWhiteSpace(slot.PlayerId)
            ? BuildDefaultPlayerId(teamId, slot.Slot)
            : slot.PlayerId.Trim();

        try
        {
            slot.IsSubmitting = true;
            slot.SubmitStateText = $"正在提交 {pendingCharacter.DisplayName}...";

            var updatedRoom = await _workspace.SelectRoleAsync(new SelectRoleRequest
            {
                Slot = slot.Slot,
                PlayerId = playerId,
                PlayerName = playerName,
                TeamId = teamId,
                CharacterId = pendingCharacter.Id
            });

            if (updatedRoom is null)
            {
                slot.SubmitStateText = _workspace.StatusMessage;
                return;
            }

            ReplaceSelectedRoom(updatedRoom);
            slot.SelectedCharacter = pendingCharacter;
            await LoadSlotPreviewImageAsync(slot);
            StatusMessage = $"{slot.SlotTitle} 已提交 {pendingCharacter.DisplayName}，等待 SignalR 同步确认。";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            slot.SubmitStateText = $"提交失败: {ex.Message}";
            StatusMessage = slot.SubmitStateText;
            _notifications.Error(ex, "提交选角失败");
        }
        finally
        {
            slot.IsSubmitting = false;
        }
    }

    /// <summary>
    /// 替换选中房间。
    /// </summary>
    private void ReplaceSelectedRoom(BpRoom room)
    {
        var existingIndex = Rooms
            .Select((value, index) => new { value, index })
            .FirstOrDefault(x => string.Equals(x.value.RoomId, room.RoomId, StringComparison.OrdinalIgnoreCase))?.index;

        if (existingIndex is not null)
        {
            Rooms[(int)existingIndex] = room;
        }

        _suppressSelectedRoomLoad = true;
        SelectedRoom = room;
        _suppressSelectedRoomLoad = false;
    }

    /// <summary>
    /// 应用空房间状态。
    /// </summary>
    private void ApplyEmptyRoomState()
    {
        SelectedRoomDisplayName = "未选择房间";
        RoomPhaseText = "阶段: 未知";
        RoomRoundText = "回合: -";
        SurvivorTeamName = "求生者方";
        HunterTeamName = "监管者方";
        SurPickList = [];
        HunPickVm = new PickSlotItem();
        HomeSurGlobalBanRecords = [];
        HomeHunGlobalBanRecords = [];
        AwaySurGlobalBanRecords = [];
        AwayHunGlobalBanRecords = [];
    }

    /// <summary>
    /// 加载槽位预览图片。
    /// </summary>
    private async Task LoadSlotPreviewImageAsync(PickSlotItem slot)
    {
        var character = slot.SelectedCharacter;
        var url = character?.PreviewImageUrl;
        var cacheKey = character?.PreviewImageRelativePath ?? url;
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            slot.SelectedCharacterPreviewImage = null;
            return;
        }

        if (_previewImageCache.TryGetValue(cacheKey, out var cachedImage))
        {
            slot.SelectedCharacterPreviewImage = cachedImage;
            return;
        }

        var localBitmap = TryLoadLocalPreviewImage(character?.PreviewImageRelativePath);
        if (localBitmap is not null)
        {
            _previewImageCache[cacheKey] = localBitmap;
            slot.SelectedCharacterPreviewImage = localBitmap;
            return;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            slot.SelectedCharacterPreviewImage = null;
            return;
        }

        try
        {
            await using var stream = await _apiClient.GetStreamAsync(url);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            var bitmap = new Bitmap(memoryStream);
            _previewImageCache[cacheKey] = bitmap;
            slot.SelectedCharacterPreviewImage = bitmap;
        }
        catch
        {
            _previewImageCache[cacheKey] = null;
            slot.SelectedCharacterPreviewImage = null;
        }
    }

    /// <summary>
    /// 尝试加载本地预览图片。
    /// </summary>
    private static Bitmap? TryLoadLocalPreviewImage(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", normalizedPath),
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", normalizedPath),
            Path.Combine(Directory.GetCurrentDirectory(), "Idvbp.Neo", "Resources", normalizedPath)
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            using var fileStream = File.OpenRead(candidate);
            using var memoryStream = new MemoryStream();
            fileStream.CopyTo(memoryStream);
            memoryStream.Position = 0;
            return new Bitmap(memoryStream);
        }

        return null;
    }

    /// <summary>
    /// 从工作区同步选中房间。
    /// </summary>
    private void SyncSelectedRoomFromWorkspace()
    {
        Rooms = _workspace.Rooms;
        OnPropertyChanged(nameof(HasRooms));

        _suppressSelectedRoomLoad = true;
        SelectedRoom = _workspace.SelectedRoom;
        _suppressSelectedRoomLoad = false;

        if (SelectedRoom is null)
        {
            ApplyEmptyRoomState();
            return;
        }

        ApplyRoom(SelectedRoom);
    }

    /// <summary>
    /// 构建默认玩家 ID。
    /// </summary>
    private static string BuildDefaultPlayerId(string teamId, string slot)
        => string.IsNullOrWhiteSpace(teamId) ? slot.ToLowerInvariant() : $"{teamId}-{slot.ToLowerInvariant()}";

    /// <summary>
    /// 构建默认队伍 ID。
    /// </summary>
    private static string BuildDefaultTeamId(PickSlotItem slot)
        => string.IsNullOrWhiteSpace(slot.TeamName)
            ? slot.RoleTitle.ToLowerInvariant()
            : slot.TeamName.Trim().Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>
    /// 处理房间事件。
    /// </summary>
    private void OnRoomEventReceived(RoomEventEnvelope envelope)
    {
        if (SelectedRoom is null || !string.Equals(envelope.RoomId, SelectedRoom.RoomId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(() => ApplyRealtimeEvent(envelope));
    }

    /// <summary>
    /// 实时重连处理。
    /// </summary>
    private Task OnRealtimeReconnectedAsync()
    {
        if (string.IsNullOrWhiteSpace(_subscribedRoomId))
        {
            return Task.CompletedTask;
        }

        return SwitchRealtimeRoomAsync(_subscribedRoomId);
    }

    /// <summary>
    /// 应用实时事件。
    /// </summary>
    private void ApplyRealtimeEvent(RoomEventEnvelope envelope)
    {
        var room = DeserializeRoomEnvelope(envelope);
        if (room is null)
        {
            return;
        }

        ApplyRoom(room);
        ReplaceSelectedRoom(room);
        StatusMessage = envelope.EventType switch
        {
            RoomEventNames.RoomSnapshot => "已收到房间快照。",
            RoomEventNames.RoleSelected => "已收到实时选角更新。",
            RoomEventNames.MatchCreated => "已收到新对局状态。",
            RoomEventNames.RoomInfoUpdated => "已收到房间信息更新。",
            RoomEventNames.BanUpdated => "已收到当前 Ban 更新。",
            RoomEventNames.GlobalBanUpdated => "已收到全局 Ban 更新。",
            RoomEventNames.PhaseUpdated => "已收到阶段更新。",
            _ => $"已收到实时事件: {envelope.EventType}"
        };
    }

    /// <summary>
    /// 反序列化房间事件信封。
    /// </summary>
    private BpRoom? DeserializeRoomEnvelope(RoomEventEnvelope envelope)
    {
        try
        {
            return envelope.EventType switch
            {
                RoomEventNames.RoomSnapshot => envelope.Payload.Deserialize<BpRoom>(),
                RoomEventNames.RoomInfoUpdated => envelope.Payload.Deserialize<BpRoom>(),
                RoomEventNames.MatchCreated => envelope.Payload.Deserialize<BpRoom>(),
                RoomEventNames.RoleSelected => MergeRoleSelectedPayload(envelope.Payload.Deserialize<RoleSelectedPayload>()),
                RoomEventNames.BanUpdated => MergeBanUpdatedPayload(envelope.Payload.Deserialize<BanUpdatedPayload>()),
                RoomEventNames.GlobalBanUpdated => MergeGlobalBanUpdatedPayload(envelope.Payload.Deserialize<GlobalBanUpdatedPayload>()),
                RoomEventNames.PhaseUpdated => MergePhaseUpdatedPayload(envelope.Payload.Deserialize<PhaseUpdatedPayload>()),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 合并角色选择载荷。
    /// </summary>
    private BpRoom? MergeRoleSelectedPayload(RoleSelectedPayload? payload)
    {
        if (payload is null || SelectedRoom is null)
        {
            return null;
        }

        SelectedRoom.CharacterPicks = payload.CharacterPicks;
        SelectedRoom.Touch();
        return SelectedRoom;
    }

    /// <summary>
    /// 合并禁用更新载荷。
    /// </summary>
    private BpRoom? MergeBanUpdatedPayload(BanUpdatedPayload? payload)
    {
        if (payload is null || SelectedRoom is null)
        {
            return null;
        }

        SelectedRoom.Bans = payload.Bans;
        SelectedRoom.CurrentRound = payload.CurrentRound;
        SelectedRoom.Touch();
        return SelectedRoom;
    }

    /// <summary>
    /// 合并全局禁用更新载荷。
    /// </summary>
    private BpRoom? MergeGlobalBanUpdatedPayload(GlobalBanUpdatedPayload? payload)
    {
        if (payload is null || SelectedRoom is null)
        {
            return null;
        }

        SelectedRoom.GlobalBans = payload.GlobalBans;
        SelectedRoom.Touch();
        return SelectedRoom;
    }

    /// <summary>
    /// 合并阶段更新载荷。
    /// </summary>
    private BpRoom? MergePhaseUpdatedPayload(PhaseUpdatedPayload? payload)
    {
        if (payload is null || SelectedRoom is null)
        {
            return null;
        }

        SelectedRoom.CurrentPhase = payload.Phase;
        SelectedRoom.Touch();
        return SelectedRoom;
    }
}
