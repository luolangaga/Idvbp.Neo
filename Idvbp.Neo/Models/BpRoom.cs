using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Models.Enums;

namespace Idvbp.Neo.Models;

/// <summary>
/// BP 房间模型，包含房间状态、队伍、禁用与选人信息。
/// </summary>
public partial class BpRoom : ObservableObject
{
    [ObservableProperty]
    private string _roomId = string.Empty;

    [ObservableProperty]
    private string _roomName = string.Empty;

    [ObservableProperty]
    private BpPhase _currentPhase = BpPhase.Waiting;

    [ObservableProperty]
    private int _currentRound = 1;

    [ObservableProperty]
    private Team _teamA = new();

    [ObservableProperty]
    private Team _teamB = new();

    [ObservableProperty]
    private MapSelection _mapSelection = new();

    [ObservableProperty]
    private CharacterPickSelection _characterPicks = new();

    [ObservableProperty]
    private BanSelection _bans = new();

    [ObservableProperty]
    private GlobalBanSelection _globalBans = new();

    [ObservableProperty]
    private MatchScore _matchScore = new();

    [ObservableProperty]
    private DateTimeOffset _createdAtUtc = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private DateTimeOffset _updatedAtUtc = DateTimeOffset.UtcNow;

    /// <summary>
    /// 开始新一轮，交换双方阵营并重置选人/禁用状态。
    /// </summary>
    public void StartNewRound()
    {
        CurrentRound++;
        (TeamA.CurrentSide, TeamB.CurrentSide) = (TeamB.CurrentSide, TeamA.CurrentSide);
        CharacterPicks = new CharacterPickSelection();
        Bans = new BanSelection();
        MapSelection = new MapSelection();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 更新最后修改时间。
    /// </summary>
    public void Touch()
    {
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
