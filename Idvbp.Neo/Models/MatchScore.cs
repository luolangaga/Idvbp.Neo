using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Models.Enums;

namespace Idvbp.Neo.Models;

public partial class MatchScore : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<RoundScore> _rounds = [];

    [ObservableProperty]
    private int _survivorMatchScore;

    [ObservableProperty]
    private int _hunterMatchScore;

    [ObservableProperty]
    private int _totalRounds;

    [ObservableProperty]
    private GameSide? _matchWinner;

    public void AddRound(RoundScore round)
    {
        Rounds.Add(round);
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        SurvivorMatchScore = 0;
        HunterMatchScore = 0;

        foreach (var round in Rounds)
        {
            if (round.RoundWinner == GameSide.Survivor)
                SurvivorMatchScore++;
            else if (round.RoundWinner == GameSide.Hunter)
                HunterMatchScore++;
        }

        MatchWinner = SurvivorMatchScore > HunterMatchScore ? GameSide.Survivor :
                      HunterMatchScore > SurvivorMatchScore ? GameSide.Hunter :
                      null;

        TotalRounds = Rounds.Count;
    }
}
