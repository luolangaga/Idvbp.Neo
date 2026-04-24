using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Models.Enums;

namespace Idvbp.Neo.Models;

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

    public void StartNewRound()
    {
        CurrentRound++;
        (TeamA.CurrentSide, TeamB.CurrentSide) = (TeamB.CurrentSide, TeamA.CurrentSide);
        CharacterPicks = new CharacterPickSelection();
        Bans = new BanSelection();
        MapSelection = new MapSelection();
    }
}
