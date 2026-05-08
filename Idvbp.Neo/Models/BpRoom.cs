using System;
using System.Collections.ObjectModel;
using System.Linq;
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
    private ObservableCollection<RoundBpState> _roundStates = [];

    [ObservableProperty]
    private DateTimeOffset _createdAtUtc = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private DateTimeOffset _updatedAtUtc = DateTimeOffset.UtcNow;

    /// <summary>
    /// 开始新一轮，交换双方阵营并重置选人/禁用状态。
    /// </summary>
    public void StartNewRound()
    {
        EnsureRoundState(CurrentRound);
        SwitchToRound(CurrentRound + 1, BpPhase.Waiting, resetGlobalBans: false);
    }

    public RoundBpState EnsureRoundState(int roundNumber)
    {
        if (roundNumber <= 0)
        {
            roundNumber = 1;
        }

        var state = FindRoundState(roundNumber);
        if (state is not null)
        {
            return state;
        }

        if (RoundStates.Count == 0 && CurrentRound == roundNumber)
        {
            state = CaptureCurrentRoundState(roundNumber);
        }
        else
        {
            var previousState = roundNumber <= 1
                ? CaptureCurrentRoundState(CurrentRound)
                : EnsureRoundState(roundNumber - 1);
            state = CreateEmptyRoundState(roundNumber, previousState);
        }

        RoundStates.Add(state);
        return state;
    }

    public void SwitchToRound(int roundNumber, BpPhase? phase = null, bool resetGlobalBans = false)
    {
        if (roundNumber <= 0)
        {
            roundNumber = 1;
        }

        StoreCurrentRoundState();
        var state = EnsureRoundState(roundNumber);
        if (phase.HasValue)
        {
            state.Phase = phase.Value;
        }

        if (resetGlobalBans)
        {
            state.GlobalBans = new GlobalBanSelection
            {
                SurvivorBanSlots = GlobalBans.SurvivorBanSlots,
                HunterBanSlots = GlobalBans.HunterBanSlots
            };
        }

        ApplyRoundState(state);
        Touch();
    }

    public void StoreCurrentRoundState()
    {
        var state = FindRoundState(CurrentRound);
        if (state is null)
        {
            RoundStates.Add(CaptureCurrentRoundState(CurrentRound));
            return;
        }

        state.Phase = CurrentPhase;
        state.TeamASide = TeamA.CurrentSide;
        state.TeamBSide = TeamB.CurrentSide;
        state.MapSelection = MapSelection;
        state.CharacterPicks = CharacterPicks;
        state.Bans = Bans;
        state.GlobalBans = GlobalBans;
    }

    /// <summary>
    /// 更新最后修改时间。
    /// </summary>
    public void Touch()
    {
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private RoundBpState? FindRoundState(int roundNumber)
        => RoundStates.FirstOrDefault(x => x.RoundNumber == roundNumber);

    private RoundBpState CaptureCurrentRoundState(int roundNumber)
        => new()
        {
            RoundNumber = roundNumber,
            Phase = CurrentPhase,
            TeamASide = TeamA.CurrentSide,
            TeamBSide = TeamB.CurrentSide,
            MapSelection = MapSelection,
            CharacterPicks = CharacterPicks,
            Bans = Bans,
            GlobalBans = GlobalBans
        };

    private RoundBpState CreateEmptyRoundState(int roundNumber, RoundBpState previousState)
        => new()
        {
            RoundNumber = roundNumber,
            Phase = BpPhase.Waiting,
            TeamASide = previousState.TeamBSide,
            TeamBSide = previousState.TeamASide,
            MapSelection = new MapSelection
            {
                BanSlotsPerSide = previousState.MapSelection.BanSlotsPerSide
            },
            CharacterPicks = new CharacterPickSelection(),
            Bans = new BanSelection
            {
                SurvivorBanSlots = previousState.Bans.SurvivorBanSlots,
                HunterBanSlots = previousState.Bans.HunterBanSlots
            },
            GlobalBans = previousState.GlobalBans
        };

    private void ApplyRoundState(RoundBpState state)
    {
        CurrentRound = state.RoundNumber;
        CurrentPhase = state.Phase;
        TeamA.CurrentSide = state.TeamASide;
        TeamB.CurrentSide = state.TeamBSide;
        MapSelection = state.MapSelection;
        CharacterPicks = state.CharacterPicks;
        Bans = state.Bans;
        GlobalBans = state.GlobalBans;
    }
}
