using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Client;
using Idvbp.Neo.Models;
using Idvbp.Neo.Models.Enums;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Services;

namespace Idvbp.Neo.Service;

/// <summary>
/// Central room workspace for REST writes, SignalR subscriptions and selected-room state.
/// </summary>
public partial class BpRoomWorkspace : ObservableObject, IDisposable
{
    private const int MinimumSwitchOverlayMilliseconds = 1500;

    private readonly BpApiClient _apiClient;
    private readonly RoomRealtimeClient _realtimeClient;
    private readonly AppNotificationService _notifications;
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private long _selectionVersion;
    private string? _subscribedRoomId;
    private string? _lastSelectedRoomId;

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

    public BpRoomWorkspace(BpApiClient apiClient, RoomRealtimeClient realtimeClient, AppNotificationService notifications)
    {
        _apiClient = apiClient;
        _realtimeClient = realtimeClient;
        _notifications = notifications;
        _realtimeClient.RoomEventReceived += OnRoomEventReceived;
        _realtimeClient.CurrentRoomChanged += OnCurrentRoomChanged;
        _realtimeClient.Reconnected += OnRealtimeReconnectedAsync;
    }

    public event Action<BpRoom?>? ActiveRoomChanged;

    [ObservableProperty]
    private ObservableCollection<BpRoom> _rooms = [];

    private BpRoom? _selectedRoom;

    public BpRoom? SelectedRoom
    {
        get => _selectedRoom;
        private set
        {
            if (SetProperty(ref _selectedRoom, value))
            {
                RaiseRoomSummaryChanged();
                ActiveRoomChanged?.Invoke(value);
            }
        }
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isSwitchingRoom;

    [ObservableProperty]
    private string _statusMessage = "未连接房间";

    public string CurrentRoomTitle => SelectedRoom is null
        ? "未选择房间"
        : $"{SelectedRoom.RoomName} / 第 {SelectedRoom.CurrentRound} 局 / {SelectedRoom.CurrentPhase}";

    public string SurvivorTeamName => GetTeamName(GameSide.Survivor, "求生者队伍");

    public string HunterTeamName => GetTeamName(GameSide.Hunter, "监管者队伍");

    public async Task RefreshRoomsAsync()
    {
        IsBusy = true;
        try
        {
            var previousRoomId = SelectedRoom?.RoomId;
            var rooms = await _apiClient.GetRoomsAsync();
            Rooms = new ObservableCollection<BpRoom>(rooms.OrderByDescending(x => x.UpdatedAtUtc));

            if (Rooms.Count == 0)
            {
                await ClearSelectedRoomAsync();
                StatusMessage = "已连接内置服务，但当前没有房间。";
                return;
            }

            var nextRoom = Rooms.FirstOrDefault(x => SameId(x.RoomId, previousRoomId)) ?? Rooms[0];
            await SwitchRoomAsync(nextRoom.RoomId, showOverlay: false, resetViewsBeforeSnapshot: false);
            StatusMessage = "已刷新房间列表。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"连接内置服务失败: {ex.Message}";
            _notifications.Error(ex, "连接内置服务失败");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<BpRoom?> CreateRoomAsync(CreateRoomRequest request)
    {
        IsBusy = true;
        try
        {
            var room = await _apiClient.CreateRoomAsync(request);
            UpsertRoom(room);
            await SwitchRoomAsync(room.RoomId, showOverlay: false, resetViewsBeforeSnapshot: false);
            StatusMessage = $"已新建比赛房间: {room.RoomName}";
            _notifications.Success(StatusMessage);
            return room;
        }
        catch (Exception ex)
        {
            StatusMessage = $"新建比赛失败: {ex.Message}";
            _notifications.Error(ex, "新建比赛失败");
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<BpRoom?> CreateNextMatchAsync(BpPhase currentPhase, bool resetGlobalBans)
    {
        var activeRoom = ResolveActiveRoom();
        if (activeRoom is null)
        {
            StatusMessage = "请先选择或新建比赛房间。";
            _notifications.Warning(StatusMessage);
            return null;
        }

        return await ExecuteRoomWriteWithRoomRetryAsync(
            room => _apiClient.CreateMatchAsync(room.RoomId, new CreateMatchRequest
            {
                CurrentPhase = currentPhase,
                ResetGlobalBans = resetGlobalBans
            }),
            room => $"已创建第 {room.CurrentRound} 局。");
    }

    public async Task<BpRoom?> AdvanceToRoundAsync(int targetRound, BpPhase currentPhase, bool resetGlobalBans)
    {
        var activeRoom = ResolveActiveRoom();
        if (activeRoom is null)
        {
            StatusMessage = "请先选择或新建比赛房间。";
            _notifications.Warning(StatusMessage);
            return null;
        }

        if (targetRound <= 0)
        {
            StatusMessage = "游戏进度必须大于 0。";
            _notifications.Warning(StatusMessage);
            return null;
        }

        var roomId = activeRoom.RoomId;
        IsSwitchingRoom = true;
        try
        {
            var room = await _apiClient.CreateMatchAsync(roomId, new CreateMatchRequest
            {
                TargetRound = targetRound,
                CurrentPhase = currentPhase,
                ResetGlobalBans = resetGlobalBans
            });
            var storedRoom = UpsertRoom(room);
            SetSelectedRoomSilently(storedRoom);

            StatusMessage = room.CurrentRound == targetRound
                ? $"已同步到第 {room.CurrentRound} 局。"
                : $"已切换显示到第 {targetRound} 局。";

            await SwitchRoomAsync(roomId, showOverlay: true, resetViewsBeforeSnapshot: true, forceRefresh: true);
            return SelectedRoom;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            RemoveRoomById(roomId);
            await RefreshRoomsAsync();
            var retryRoom = ResolveActiveRoom();
            if (retryRoom is null)
            {
                StatusMessage = "当前房间已不存在，请重新选择或新建比赛房间。";
                _notifications.Warning(StatusMessage);
                return null;
            }

            try
            {
                var room = await _apiClient.CreateMatchAsync(retryRoom.RoomId, new CreateMatchRequest
                {
                    TargetRound = targetRound,
                    CurrentPhase = currentPhase,
                    ResetGlobalBans = resetGlobalBans
                });
                var storedRoom = UpsertRoom(room);
                SetSelectedRoomSilently(storedRoom);
                StatusMessage = room.CurrentRound == targetRound
                    ? $"已同步到第 {room.CurrentRound} 局。"
                    : $"已切换显示到第 {targetRound} 局。";
                await SwitchRoomAsync(retryRoom.RoomId, showOverlay: true, resetViewsBeforeSnapshot: true, forceRefresh: true);
                return SelectedRoom;
            }
            catch (Exception retryEx)
            {
                StatusMessage = $"同步游戏进度失败: {retryEx.Message}";
                _notifications.Error(retryEx, "同步游戏进度失败");
                return null;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"同步游戏进度失败: {ex.Message}";
            _notifications.Error(ex, "同步游戏进度失败");
            return null;
        }
        finally
        {
            IsSwitchingRoom = false;
        }
    }

    public Task<BpRoom?> UpdateTeamsAsync(UpdateRoomTeamsRequest request)
    {
        var activeRoom = ResolveActiveRoom();
        if (activeRoom is null)
        {
            StatusMessage = "请先选择或新建比赛房间。";
            _notifications.Warning(StatusMessage);
            return Task.FromResult<BpRoom?>(null);
        }

        return ExecuteRoomWriteWithRoomRetryAsync(
            room => _apiClient.UpdateTeamsAsync(room.RoomId, request),
            _ => "队伍信息已保存。");
    }

    public Task<BpRoom?> UpdateMapAsync(UpdateMapRequest request)
    {
        var activeRoom = ResolveActiveRoom();
        if (activeRoom is null)
        {
            StatusMessage = "请先选择或新建比赛房间。";
            _notifications.Warning(StatusMessage);
            return Task.FromResult<BpRoom?>(null);
        }

        return ExecuteRoomWriteWithRoomRetryAsync(
            room => _apiClient.UpdateMapAsync(room.RoomId, request),
            room => room.MapSelection.PickedMap is null
                ? "地图状态已更新。"
                : $"已选择地图: {room.MapSelection.PickedMap.Name}");
    }

    public Task<BpRoom?> AddBanAsync(AddBanRequest request, bool isGlobalBan)
    {
        var activeRoom = ResolveActiveRoom();
        if (activeRoom is null)
        {
            StatusMessage = "请先选择或新建比赛房间。";
            _notifications.Warning(StatusMessage);
            return Task.FromResult<BpRoom?>(null);
        }

        return ExecuteRoomWriteWithRoomRetryAsync(
            room => isGlobalBan
                ? _apiClient.AddGlobalBanAsync(room.RoomId, request)
                : _apiClient.AddBanAsync(room.RoomId, request),
            _ => isGlobalBan ? "全局 Ban 已提交。" : "当前局 Ban 已提交。");
    }

    public Task<BpRoom?> AddMapBanAsync(AddMapBanRequest request)
    {
        var activeRoom = ResolveActiveRoom();
        if (activeRoom is null)
        {
            StatusMessage = "请先选择或新建比赛房间。";
            _notifications.Warning(StatusMessage);
            return Task.FromResult<BpRoom?>(null);
        }

        return ExecuteRoomWriteWithRoomRetryAsync(
            room => _apiClient.AddMapBanAsync(room.RoomId, request),
            _ => "地图 Ban 已提交。");
    }

    public Task<BpRoom?> SelectRoleAsync(SelectRoleRequest request)
    {
        var room = ResolveActiveRoom();
        if (room is null)
        {
            StatusMessage = "请先选择或新建比赛房间。";
            _notifications.Warning(StatusMessage);
            return Task.FromResult<BpRoom?>(null);
        }

        return ExecuteRoomWriteWithRoomRetryAsync(
            r => _apiClient.SelectRoleAsync(r.RoomId, request),
            _ => "选角已提交。");
    }

    public void AcceptServerRoom(BpRoom room)
    {
        var storedRoom = UpsertRoom(room);
        if (SelectedRoom is null || SameId(SelectedRoom.RoomId, storedRoom.RoomId) || SameId(_subscribedRoomId, storedRoom.RoomId))
        {
            SetSelectedRoomSilently(storedRoom);
        }

        StatusMessage = "已同步服务端房间状态。";
    }

    public async Task SwitchRoomAsync(string? roomId, bool showOverlay = true, bool resetViewsBeforeSnapshot = true, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return;
        }

        if (!forceRefresh && SameId(roomId, _subscribedRoomId) && SameId(roomId, SelectedRoom?.RoomId))
        {
            return;
        }

        var version = Interlocked.Increment(ref _selectionVersion);
        await _switchGate.WaitAsync();
        var switchTimer = Stopwatch.StartNew();

        if (showOverlay)
        {
            IsSwitchingRoom = true;
        }

        try
        {
            StatusMessage = "正在切换房间...";

            if (!string.IsNullOrWhiteSpace(_subscribedRoomId) && !SameId(_subscribedRoomId, roomId))
            {
                await _realtimeClient.LeaveRoomAsync(_subscribedRoomId);
            }

            if (resetViewsBeforeSnapshot)
            {
                ResetActiveRoomViews();
            }

            await _realtimeClient.SubscribeToRoomAsync(roomId, RealtimeEventTypes);
            _subscribedRoomId = roomId;

            var snapshot = await _apiClient.GetRoomAsync(roomId);
            if (version != Interlocked.Read(ref _selectionVersion))
            {
                return;
            }

            if (snapshot is not null)
            {
                var storedSnapshot = UpsertRoom(snapshot);
                SetSelectedRoomSilently(storedSnapshot);
                await PublishCurrentRoomSelectionAsync(snapshot.RoomId);
            }

            await _realtimeClient.RequestRoomSnapshotAsync(roomId);
            StatusMessage = snapshot is null ? $"房间 {roomId} 不存在。" : $"已切换到房间: {snapshot.RoomName}";
            if (snapshot is null)
            {
                RemoveRoomById(roomId);
                _notifications.Warning(StatusMessage);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            RemoveRoomById(roomId);
            StatusMessage = $"切换房间失败: {ex.Message}";
            _notifications.Warning(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"切换房间失败: {ex.Message}";
            _notifications.Error(ex, "切换房间失败");
        }
        finally
        {
            if (showOverlay)
            {
                var remainingDelay = MinimumSwitchOverlayMilliseconds - (int)switchTimer.ElapsedMilliseconds;
                if (remainingDelay > 0)
                {
                    await Task.Delay(remainingDelay);
                }

                IsSwitchingRoom = false;
            }

            _switchGate.Release();
        }
    }

    private async Task ClearSelectedRoomAsync()
    {
        Interlocked.Increment(ref _selectionVersion);
        await _switchGate.WaitAsync();
        try
        {
            if (!string.IsNullOrWhiteSpace(_subscribedRoomId))
            {
                await _realtimeClient.LeaveRoomAsync(_subscribedRoomId);
            }

            _subscribedRoomId = null;
            SetSelectedRoomSilently(null);
        }
        finally
        {
            _switchGate.Release();
        }
    }

    private async Task<BpRoom?> ExecuteRoomWriteWithRoomRetryAsync(Func<BpRoom, Task<BpRoom>> action, Func<BpRoom, string> successMessage)
    {
        var activeRoom = ResolveActiveRoom();
        if (activeRoom is null)
        {
            StatusMessage = "请先选择或新建比赛房间。";
            _notifications.Warning(StatusMessage);
            IsBusy = false;
            return null;
        }

        IsBusy = true;
        try
        {
            var room = await action(activeRoom);
            AcceptServerRoom(room);
            StatusMessage = successMessage(room);
            _notifications.Success(StatusMessage);
            return room;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            RemoveRoomById(activeRoom.RoomId);
            await RefreshRoomsAsync();
            var retryRoom = ResolveActiveRoom();
            if (retryRoom is null)
            {
                StatusMessage = "当前房间已不存在，请重新选择或新建比赛房间。";
                _notifications.Warning(StatusMessage);
                return null;
            }

            try
            {
                var room = await action(retryRoom);
                AcceptServerRoom(room);
                StatusMessage = successMessage(room);
                _notifications.Success(StatusMessage);
                return room;
            }
            catch (Exception retryEx)
            {
                StatusMessage = $"操作失败: {retryEx.Message}";
                _notifications.Error(retryEx, "操作失败");
                return null;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"操作失败: {ex.Message}";
            _notifications.Error(ex, "操作失败");
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task OnRealtimeReconnectedAsync()
        => string.IsNullOrWhiteSpace(_subscribedRoomId)
            ? Task.CompletedTask
            : SwitchRoomAsync(_subscribedRoomId, showOverlay: false, resetViewsBeforeSnapshot: false);

    private void OnCurrentRoomChanged(CurrentRoomPayload payload)
    {
        // Current-room broadcasts are for overlays/observers. The desktop controller keeps
        // its room selection local and only changes it through explicit user actions.
    }

    private async Task PublishCurrentRoomSelectionAsync(string roomId)
    {
        try
        {
            await _apiClient.SetCurrentRoomAsync(roomId);
        }
        catch
        {
            // Room switching itself already succeeded; current-room broadcast is best effort.
        }
    }

    private void OnRoomEventReceived(RoomEventEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(_subscribedRoomId) || !SameId(_subscribedRoomId, envelope.RoomId))
        {
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(() => ApplyRoomEvent(envelope));
    }

    private void ApplyRoomEvent(RoomEventEnvelope envelope)
    {
        if (!SameId(_subscribedRoomId, envelope.RoomId))
        {
            return;
        }

        if (SelectedRoom is not null
            && !SameId(SelectedRoom.RoomId, envelope.RoomId)
            && envelope.EventType is not (RoomEventNames.RoomSnapshot or RoomEventNames.RoomInfoUpdated or RoomEventNames.MatchCreated))
        {
            return;
        }

        var room = DeserializeRoomEvent(envelope);
        if (room is null)
        {
            return;
        }

        var storedRoom = UpsertRoom(room);
        SetSelectedRoomSilently(storedRoom);
        StatusMessage = envelope.EventType switch
        {
            RoomEventNames.RoomSnapshot => "已收到房间快照。",
            RoomEventNames.RoleSelected => "已收到实时选角更新。",
            RoomEventNames.MatchCreated => "已收到新对局状态。",
            RoomEventNames.RoomInfoUpdated => "已收到房间信息更新。",
            RoomEventNames.MapUpdated => "已收到地图 BP 更新。",
            RoomEventNames.BanUpdated => "已收到当前 Ban 更新。",
            RoomEventNames.GlobalBanUpdated => "已收到全局 Ban 更新。",
            RoomEventNames.PhaseUpdated => "已收到阶段更新。",
            _ => $"已收到事件: {envelope.EventType}"
        };
    }

    private BpRoom? DeserializeRoomEvent(RoomEventEnvelope envelope)
    {
        try
        {
            return envelope.EventType switch
            {
                RoomEventNames.RoomSnapshot => envelope.Payload.Deserialize<BpRoom>(),
                RoomEventNames.RoomInfoUpdated => envelope.Payload.Deserialize<BpRoom>(),
                RoomEventNames.MatchCreated => envelope.Payload.Deserialize<BpRoom>(),
                RoomEventNames.MapUpdated => MergeMapUpdated(envelope.Payload.Deserialize<MapUpdatedPayload>()),
                RoomEventNames.RoleSelected => MergeRoleSelected(envelope.Payload.Deserialize<RoleSelectedPayload>()),
                RoomEventNames.BanUpdated => MergeBanUpdated(envelope.Payload.Deserialize<BanUpdatedPayload>()),
                RoomEventNames.GlobalBanUpdated => MergeGlobalBanUpdated(envelope.Payload.Deserialize<GlobalBanUpdatedPayload>()),
                RoomEventNames.PhaseUpdated => MergePhaseUpdated(envelope.Payload.Deserialize<PhaseUpdatedPayload>()),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private BpRoom? MergeMapUpdated(MapUpdatedPayload? payload)
    {
        if (payload is null || SelectedRoom is null)
        {
            return null;
        }

        SelectedRoom.MapSelection = payload.MapSelection;
        SelectedRoom.CurrentRound = payload.CurrentRound;
        SelectedRoom.Touch();
        return SelectedRoom;
    }

    private BpRoom? MergeRoleSelected(RoleSelectedPayload? payload)
    {
        if (payload is null || SelectedRoom is null)
        {
            return null;
        }

        SelectedRoom.CharacterPicks = payload.CharacterPicks;
        SelectedRoom.Touch();
        return SelectedRoom;
    }

    private BpRoom? MergeBanUpdated(BanUpdatedPayload? payload)
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

    private BpRoom? MergeGlobalBanUpdated(GlobalBanUpdatedPayload? payload)
    {
        if (payload is null || SelectedRoom is null)
        {
            return null;
        }

        SelectedRoom.GlobalBans = payload.GlobalBans;
        SelectedRoom.Touch();
        return SelectedRoom;
    }

    private BpRoom? MergePhaseUpdated(PhaseUpdatedPayload? payload)
    {
        if (payload is null || SelectedRoom is null)
        {
            return null;
        }

        SelectedRoom.CurrentPhase = payload.Phase;
        SelectedRoom.Touch();
        return SelectedRoom;
    }

    private void SetSelectedRoomSilently(BpRoom? room)
    {
        if (room is not null)
        {
            RememberSelectedRoom(room);
        }

        SelectedRoom = room;
    }

    private void ResetActiveRoomViews()
    {
        RaiseRoomSummaryChanged();
    }

    private void RaiseRoomSummaryChanged()
    {
        OnPropertyChanged(nameof(CurrentRoomTitle));
        OnPropertyChanged(nameof(SurvivorTeamName));
        OnPropertyChanged(nameof(HunterTeamName));
    }

    private BpRoom UpsertRoom(BpRoom room)
    {
        var existing = Rooms.Select((value, index) => new { value, index })
            .FirstOrDefault(x => SameId(x.value.RoomId, room.RoomId));

        if (existing is null)
        {
            Rooms.Insert(0, room);
            return room;
        }

        var targetRoom = existing.value;
        CopyRoomState(targetRoom, room);
        return targetRoom;
    }

    private void RemoveRoomById(string roomId)
    {
        var room = Rooms.FirstOrDefault(x => SameId(x.RoomId, roomId));
        if (room is not null)
        {
            Rooms.Remove(room);
        }

        if (SelectedRoom is not null && SameId(SelectedRoom.RoomId, roomId))
        {
            SelectedRoom = Rooms.FirstOrDefault();
            if (SelectedRoom is null)
            {
                _subscribedRoomId = null;
            }
        }
    }

    private static void CopyRoomState(BpRoom target, BpRoom source)
    {
        if (ReferenceEquals(target, source))
        {
            return;
        }

        target.RoomId = source.RoomId;
        target.RoomName = source.RoomName;
        target.CurrentPhase = source.CurrentPhase;
        target.CurrentRound = source.CurrentRound;
        target.TeamA = source.TeamA;
        target.TeamB = source.TeamB;
        target.MapSelection = source.MapSelection;
        target.CharacterPicks = source.CharacterPicks;
        target.Bans = source.Bans;
        target.GlobalBans = source.GlobalBans;
        target.MatchScore = source.MatchScore;
        target.RoundStates = source.RoundStates;
        target.CreatedAtUtc = source.CreatedAtUtc;
        target.UpdatedAtUtc = source.UpdatedAtUtc;
    }

    private BpRoom? ResolveActiveRoom()
    {
        if (SelectedRoom is not null)
        {
            return SelectedRoom;
        }

        var roomId = !string.IsNullOrWhiteSpace(_subscribedRoomId)
            ? _subscribedRoomId
            : _lastSelectedRoomId;

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return null;
        }

        return Rooms.FirstOrDefault(x => SameId(x.RoomId, roomId));
    }

    private void RememberSelectedRoom(BpRoom room)
    {
        if (!string.IsNullOrWhiteSpace(room.RoomId))
        {
            _lastSelectedRoomId = room.RoomId;
        }
    }

    private string GetTeamName(GameSide side, string fallback)
    {
        if (SelectedRoom is null)
        {
            return fallback;
        }

        var team = SelectedRoom.TeamA.CurrentSide == side ? SelectedRoom.TeamA : SelectedRoom.TeamB;
        return string.IsNullOrWhiteSpace(team.Name) ? fallback : team.Name;
    }

    private static bool SameId(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _realtimeClient.RoomEventReceived -= OnRoomEventReceived;
        _realtimeClient.CurrentRoomChanged -= OnCurrentRoomChanged;
        _realtimeClient.Reconnected -= OnRealtimeReconnectedAsync;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            _realtimeClient.DisposeAsync().AsTask().Wait(cts.Token);
        }
        catch
        {
        }

        _switchGate.Dispose();
    }
}
