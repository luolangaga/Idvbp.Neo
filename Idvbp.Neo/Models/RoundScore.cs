using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Models.Enums;

namespace Idvbp.Neo.Models;

public partial class RoundScore : ObservableObject
{
    [ObservableProperty]
    private int _roundNumber;

    [ObservableProperty]
    private RoundHalfScore _firstHalf = new();

    [ObservableProperty]
    private RoundHalfScore _secondHalf = new();

    public int SurvivorRoundScore => FirstHalf.SurvivorScore + SecondHalf.SurvivorScore;
    public int HunterRoundScore => FirstHalf.HunterScore + SecondHalf.HunterScore;

    public GameSide? RoundWinner =>
        SurvivorRoundScore > HunterRoundScore ? GameSide.Survivor :
        HunterRoundScore > SurvivorRoundScore ? GameSide.Hunter :
        null;
}
