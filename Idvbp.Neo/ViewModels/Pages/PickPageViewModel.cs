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

namespace Idvbp.Neo.ViewModels.Pages;

public sealed class CharacterOptionItem
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string? PreviewImageUrl { get; init; }

    public string? PreviewImageRelativePath { get; init; }

    public override string ToString() => DisplayName;
}

public partial class GlobalBanRecordItem : ObservableObject
{
    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private string _characterId = string.Empty;

    [ObservableProperty]
    private string _characterName = string.Empty;
}

public partial class PickSlotItem : ObservableObject
{
    private bool _suppressAutoSubmit;

    public Func<PickSlotItem, Task>? SelectionChangedAsync { get; set; }

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
    private IReadOnlyList<CharacterOptionItem> _availableCharacters = Array.Empty<CharacterOptionItem>();

    [ObservableProperty]
    private CharacterOptionItem? _selectedCharacter;

    [ObservableProperty]
    private Bitmap? _selectedCharacterPreviewImage;

    [ObservableProperty]
    private bool _isSubmitting;

    [ObservableProperty]
    private string _submitStateText = string.Empty;

    public void SetSelection(CharacterOptionItem? character, bool suppressAutoSubmit)
    {
        _suppressAutoSubmit = suppressAutoSubmit;
        SelectedCharacter = character;
        _suppressAutoSubmit = false;
    }

    partial void OnSelectedCharacterChanged(CharacterOptionItem? value)
    {
        SelectedCharacterPreviewImage = null;

        if (_suppressAutoSubmit || value is null || SelectionChangedAsync is null)
        {
            return;
        }

        _ = SelectionChangedAsync(this);
    }
}

public partial class PickPageViewModel : ViewModelBase
{
    private readonly BpApiClient _apiClient;
    private readonly RoomRealtimeClient _realtimeClient;
    private readonly BpRoomWorkspace _workspace;
    private readonly Dictionary<string, CharacterOptionItem> _characterLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap?> _previewImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _slotSubmitDebounceMap = new(StringComparer.OrdinalIgnoreCase);
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
        RoomEventNames.RoleSelected,
        RoomEventNames.BanUpdated,
        RoomEventNames.GlobalBanUpdated,
        RoomEventNames.PhaseUpdated
    ];

    public PickPageViewModel(BpApiClient apiClient, RoomRealtimeClient realtimeClient, BpRoomWorkspace workspace)
    {
        _apiClient = apiClient;
        _realtimeClient = realtimeClient;
        _workspace = workspace;
        HunPickVm.SelectionChangedAsync = QueueSlotSubmissionAsync;
        _realtimeClient.RoomEventReceived += OnRoomEventReceived;
        _realtimeClient.Reconnected += OnRealtimeReconnectedAsync;
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

    public bool HasRooms => Rooms.Count > 0;

    partial void OnSelectedRoomChanged(BpRoom? value)
    {
        if (_suppressSelectedRoomLoad)
        {
            return;
        }

        _workspace.SetSelectedRoom(value);
        if (value is not null)
        {
            ApplyRoom(value);
        }
    }

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
                return;
            }

            SyncSelectedRoomFromWorkspace();
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _isRefreshing = false;
        }
    }

    [RelayCommand]
    private Task SubmitSlotAsync(PickSlotItem? slot)
        => slot is null ? Task.CompletedTask : SubmitSlotCoreAsync(slot, CancellationToken.None);

    private async Task InitializeAsync()
    {
        await RefreshDataAsync();
    }

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
        _hasLoadedCharacterCatalog = true;
    }

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
            PreviewImageUrl = _apiClient.ToAbsoluteUrl(primaryImage?.Url),
            PreviewImageRelativePath = primaryImage?.RelativePath
        };
    }

    private async Task SwitchRealtimeRoomAsync(string? roomId)
    {
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
        }
        finally
        {
            IsBusy = false;
        }
    }

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

    private PickSlotItem CreateSurvivorSlot(string slot, int seatNumber, Player player, Team team)
    {
        var slotItem = CreateSlot(slot, $"求生者 {seatNumber}", "求生者", seatNumber, player, team, _survivorCharacters);
        slotItem.SelectionChangedAsync = QueueSlotSubmissionAsync;
        return slotItem;
    }

    private void EnsureSurvivorSlots()
    {
        if (SurPickList.Count == 4)
        {
            return;
        }

        SurPickList = new ObservableCollection<PickSlotItem>
        {
            new() { Slot = "Survivor1", SelectionChangedAsync = QueueSlotSubmissionAsync },
            new() { Slot = "Survivor2", SelectionChangedAsync = QueueSlotSubmissionAsync },
            new() { Slot = "Survivor3", SelectionChangedAsync = QueueSlotSubmissionAsync },
            new() { Slot = "Survivor4", SelectionChangedAsync = QueueSlotSubmissionAsync }
        };
    }

    private void UpdateSlotFromRoom(PickSlotItem slotItem, string slotTitle, string roleTitle, int seatNumber, Player player, Team team, IReadOnlyList<CharacterOptionItem> characters)
    {
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
        slotItem.AvailableCharacters = characters;
        slotItem.SelectionChangedAsync = QueueSlotSubmissionAsync;
        slotItem.SubmitStateText = string.IsNullOrWhiteSpace(player.CharacterId) ? string.Empty : "已同步";
        slotItem.SetSelection(TryGetCharacter(player.CharacterId), suppressAutoSubmit: true);
        _ = LoadSlotPreviewImageAsync(slotItem);
    }

    private PickSlotItem CreateHunterSlot(Player player, Team team)
    {
        var slotItem = CreateSlot("Hunter", "监管者", "监管者", 1, player, team, _hunterCharacters);
        slotItem.SelectionChangedAsync = QueueSlotSubmissionAsync;
        return slotItem;
    }

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
            AvailableCharacters = characters,
            SubmitStateText = string.IsNullOrWhiteSpace(player.CharacterId) ? "未提交选角" : "已同步当前房间选角"
        };
        slotItem.SetSelection(TryGetCharacter(player.CharacterId), suppressAutoSubmit: true);
        return slotItem;
    }

    private ObservableCollection<GlobalBanRecordItem> BuildBanRecords(IEnumerable<PickBanEntry> bans)
        => new(bans
            .OrderBy(x => x.Order)
            .Select(x => new GlobalBanRecordItem
            {
                Order = x.Order,
                CharacterId = x.CharacterId,
                CharacterName = ResolveCharacterName(x.CharacterId)
            }));

    private CharacterOptionItem? TryGetCharacter(string? characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return null;
        }

        return _characterLookup.GetValueOrDefault(characterId);
    }

    private string ResolveCharacterName(string? characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return "未记录";
        }

        return TryGetCharacter(characterId)?.DisplayName ?? characterId;
    }

    private async Task QueueSlotSubmissionAsync(PickSlotItem slot)
    {
        await LoadSlotPreviewImageAsync(slot);

        if (SelectedRoom is null)
        {
            slot.SubmitStateText = "请先选择房间。";
            return;
        }

        if (_slotSubmitDebounceMap.TryGetValue(slot.Slot, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _slotSubmitDebounceMap[slot.Slot] = cts;

        try
        {
            await Task.Delay(180, cts.Token);
            await SubmitSlotCoreAsync(slot, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_slotSubmitDebounceMap.TryGetValue(slot.Slot, out var currentCts) && ReferenceEquals(currentCts, cts))
            {
                _slotSubmitDebounceMap.Remove(slot.Slot);
            }

            cts.Dispose();
        }
    }

    private async Task SubmitSlotCoreAsync(PickSlotItem slot, CancellationToken cancellationToken)
    {
        if (SelectedRoom is null)
        {
            slot.SubmitStateText = "当前没有可用房间。";
            return;
        }

        if (slot.SelectedCharacter is null)
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
            slot.SubmitStateText = $"正在提交 {slot.SelectedCharacter.DisplayName}...";

            var updatedRoom = await _apiClient.SelectRoleAsync(SelectedRoom.RoomId, new SelectRoleRequest
            {
                Slot = slot.Slot,
                PlayerId = playerId,
                PlayerName = playerName,
                TeamId = teamId,
                CharacterId = slot.SelectedCharacter.Id
            }, cancellationToken);

            ReplaceSelectedRoom(updatedRoom);
            _workspace.AcceptServerRoom(updatedRoom);
            StatusMessage = $"{slot.SlotTitle} 已提交 {slot.SelectedCharacter.DisplayName}，等待 SignalR 同步确认。";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            slot.SubmitStateText = $"提交失败: {ex.Message}";
            StatusMessage = slot.SubmitStateText;
        }
        finally
        {
            slot.IsSubmitting = false;
        }
    }

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

    private static string BuildDefaultPlayerId(string teamId, string slot)
        => string.IsNullOrWhiteSpace(teamId) ? slot.ToLowerInvariant() : $"{teamId}-{slot.ToLowerInvariant()}";

    private static string BuildDefaultTeamId(PickSlotItem slot)
        => string.IsNullOrWhiteSpace(slot.TeamName)
            ? slot.RoleTitle.ToLowerInvariant()
            : slot.TeamName.Trim().Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant();

    private void OnRoomEventReceived(RoomEventEnvelope envelope)
    {
        if (SelectedRoom is null || !string.Equals(envelope.RoomId, SelectedRoom.RoomId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(() => ApplyRealtimeEvent(envelope));
    }

    private Task OnRealtimeReconnectedAsync()
    {
        if (string.IsNullOrWhiteSpace(_subscribedRoomId))
        {
            return Task.CompletedTask;
        }

        return SwitchRealtimeRoomAsync(_subscribedRoomId);
    }

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
