using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Client;
using Idvbp.Neo.Models;
using Idvbp.Neo.Models.Enums;
using Idvbp.Neo.Server.Contracts;

namespace Idvbp.Neo.Service;

/// <summary>
/// BP 房间工作区，管理房间列表、实时事件订阅与状态同步。
/// </summary>
public partial class BpRoomWorkspace : ObservableObject
{
    private readonly BpApiClient _apiClient;
    private readonly RoomRealtimeClient _realtimeClient;
    private bool _suppressSelectedRoomChanged;
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

    /// <summary>
    /// 初始化 BP 房间工作区。
    /// </summary>
    /// <param name="apiClient">BP API 客户端。</param>
    /// <param name="realtimeClient">房间实时客户端。</param>
    public BpRoomWorkspace(BpApiClient apiClient, RoomRealtimeClient realtimeClient)
    {
        _apiClient = apiClient;
        _realtimeClient = realtimeClient;
        _realtimeClient.RoomEventReceived += OnRoomEventReceived;
        _realtimeClient.Reconnected += OnRealtimeReconnectedAsync;
    }

    [ObservableProperty]
    private ObservableCollection<BpRoom> _rooms = [];

    [ObservableProperty]
    private BpRoom? _selectedRoom;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "未连接房间";

    /// <summary>
    /// 当前房间标题。
    /// </summary>
    public string CurrentRoomTitle => SelectedRoom is null
        ? "未选择房间"
        : $"{SelectedRoom.RoomName} / 第 {SelectedRoom.CurrentRound} 局 / {SelectedRoom.CurrentPhase}";

    /// <summary>
    /// 求生者队伍名称。
    /// </summary>
    public string SurvivorTeamName => GetTeamName(GameSide.Survivor, "求生者队伍");

    /// <summary>
    /// 监管者队伍名称。
    /// </summary>
    public string HunterTeamName => GetTeamName(GameSide.Hunter, "监管者队伍");

    partial void OnSelectedRoomChanged(BpRoom? value)
    {
        OnPropertyChanged(nameof(CurrentRoomTitle));
        OnPropertyChanged(nameof(SurvivorTeamName));
        OnPropertyChanged(nameof(HunterTeamName));

        if (_suppressSelectedRoomChanged)
        {
            return;
        }

        _ = SubscribeSelectedRoomAsync(value?.RoomId);
    }

    /// <summary>
    /// 刷新房间列表。
    /// </summary>
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
                SetSelectedRoom(null);
                StatusMessage = "内置服务器已连接，但当前没有房间。";
                return;
            }

            var nextRoom = Rooms.FirstOrDefault(x => string.Equals(x.RoomId, previousRoomId, StringComparison.OrdinalIgnoreCase)) ?? Rooms[0];
            SetSelectedRoom(nextRoom);
            await SubscribeSelectedRoomAsync(nextRoom.RoomId);
            StatusMessage = "已从内置服务器刷新房间列表。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"连接内置服务器失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 创建新房间。
    /// </summary>
    public async Task<BpRoom?> CreateRoomAsync(CreateRoomRequest request)
    {
        IsBusy = true;
        try
        {
            var room = await _apiClient.CreateRoomAsync(request);
            UpsertRoom(room);
            SetSelectedRoom(room);
            await SubscribeSelectedRoomAsync(room.RoomId);
            StatusMessage = $"已新建比赛房间: {room.RoomName}";
            return room;
        }
        catch (Exception ex)
        {
            StatusMessage = $"新建比赛失败: {ex.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 创建下一局对局。
    /// </summary>
    public async Task<BpRoom?> CreateNextMatchAsync(BpPhase currentPhase, bool resetGlobalBans)
    {
        if (SelectedRoom is null)
        {
            StatusMessage = "请先选择或新建比赛房间。";
            return null;
        }

        IsBusy = true;
        try
        {
            var room = await _apiClient.CreateMatchAsync(SelectedRoom.RoomId, new CreateMatchRequest
            {
                CurrentPhase = currentPhase,
                ResetGlobalBans = resetGlobalBans
            });
            UpsertRoom(room);
            SetSelectedRoom(room);
            StatusMessage = $"已创建第 {room.CurrentRound} 局。";
            return room;
        }
        catch (Exception ex)
        {
            StatusMessage = $"新建对局失败: {ex.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 更新队伍信息。
    /// </summary>
    public async Task<BpRoom?> UpdateTeamsAsync(UpdateRoomTeamsRequest request)
    {
        if (SelectedRoom is null)
        {
            StatusMessage = "请先选择或新建比赛房间。";
            return null;
        }

        IsBusy = true;
        try
        {
            var room = await _apiClient.UpdateTeamsAsync(SelectedRoom.RoomId, request);
            UpsertRoom(room);
            SetSelectedRoom(room);
            StatusMessage = "队伍信息已保存。";
            return room;
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存队伍信息失败: {ex.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 设置当前选中房间（不触发订阅变更）。
    /// </summary>
    public void SetSelectedRoom(BpRoom? room)
    {
        _suppressSelectedRoomChanged = true;
        SelectedRoom = room;
        _suppressSelectedRoomChanged = false;
        OnSelectedRoomChanged(room);
    }

    /// <summary>
    /// 接受服务端推送的房间状态。
    /// </summary>
    public void AcceptServerRoom(BpRoom room)
    {
        UpsertRoom(room);
        SetSelectedRoom(room);
    }

    /// <summary>
    /// 订阅选中房间的实时事件。
    /// </summary>
    private async Task SubscribeSelectedRoomAsync(string? roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_subscribedRoomId) && !string.Equals(_subscribedRoomId, roomId, StringComparison.OrdinalIgnoreCase))
        {
            await _realtimeClient.LeaveRoomAsync(_subscribedRoomId);
        }

        await _realtimeClient.SubscribeToRoomAsync(roomId, RealtimeEventTypes);
        _subscribedRoomId = roomId;
        await _realtimeClient.RequestRoomSnapshotAsync(roomId);
    }

    /// <summary>
    /// 处理接收到的房间事件。
    /// </summary>
    private void OnRoomEventReceived(RoomEventEnvelope envelope)
    {
        if (SelectedRoom is null || !string.Equals(SelectedRoom.RoomId, envelope.RoomId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(() => ApplyRoomEvent(envelope));
    }

    /// <summary>
    /// 重连后重新订阅。
    /// </summary>
    private Task OnRealtimeReconnectedAsync()
        => string.IsNullOrWhiteSpace(_subscribedRoomId) ? Task.CompletedTask : SubscribeSelectedRoomAsync(_subscribedRoomId);

    /// <summary>
    /// 应用房间事件到当前状态。
    /// </summary>
    private void ApplyRoomEvent(RoomEventEnvelope envelope)
    {
        var room = DeserializeRoomEvent(envelope);
        if (room is null)
        {
            return;
        }

        UpsertRoom(room);
        SetSelectedRoom(room);
        StatusMessage = envelope.EventType switch
        {
            RoomEventNames.RoomSnapshot => "已收到房间快照。",
            RoomEventNames.RoleSelected => "已收到实时选角更新。",
            RoomEventNames.MatchCreated => "已收到新对局状态。",
            RoomEventNames.RoomInfoUpdated => "已收到房间信息更新。",
            RoomEventNames.BanUpdated => "已收到 Ban 更新。",
            RoomEventNames.GlobalBanUpdated => "已收到全局 Ban 更新。",
            RoomEventNames.PhaseUpdated => "已收到阶段更新。",
            _ => $"已收到事件: {envelope.EventType}"
        };
    }

    /// <summary>
    /// 反序列化房间事件载荷。
    /// </summary>
    private BpRoom? DeserializeRoomEvent(RoomEventEnvelope envelope)
    {
        try
        {
            return envelope.EventType switch
            {
                RoomEventNames.RoomSnapshot => envelope.Payload.Deserialize<BpRoom>(),
                RoomEventNames.RoomInfoUpdated => envelope.Payload.Deserialize<BpRoom>(),
                RoomEventNames.MatchCreated => envelope.Payload.Deserialize<BpRoom>(),
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

    /// <summary>
    /// 合并角色选择事件到当前房间。
    /// </summary>
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

    /// <summary>
    /// 合并禁用更新事件到当前房间。
    /// </summary>
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

    /// <summary>
    /// 合并全局禁用更新事件到当前房间。
    /// </summary>
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

    /// <summary>
    /// 合并阶段更新事件到当前房间。
    /// </summary>
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

    /// <summary>
    /// 插入或更新房间到列表。
    /// </summary>
    private void UpsertRoom(BpRoom room)
    {
        var existing = Rooms.Select((value, index) => new { value, index })
            .FirstOrDefault(x => string.Equals(x.value.RoomId, room.RoomId, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            Rooms.Insert(0, room);
            return;
        }

        Rooms[existing.index] = room;
    }

    /// <summary>
    /// 获取指定阵营的队伍名称。
    /// </summary>
    private string GetTeamName(GameSide side, string fallback)
    {
        if (SelectedRoom is null)
        {
            return fallback;
        }

        var team = SelectedRoom.TeamA.CurrentSide == side ? SelectedRoom.TeamA : SelectedRoom.TeamB;
        return string.IsNullOrWhiteSpace(team.Name) ? fallback : team.Name;
    }
}
