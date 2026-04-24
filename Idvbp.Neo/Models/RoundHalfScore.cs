using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

public partial class RoundHalfScore : ObservableObject
{
    [ObservableProperty]
    private int _survivorScore;

    [ObservableProperty]
    private int _hunterScore;
}
