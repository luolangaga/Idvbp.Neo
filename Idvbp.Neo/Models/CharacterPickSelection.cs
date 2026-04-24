using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

public partial class CharacterPickSelection : ObservableObject
{
    [ObservableProperty]
    private Player _survivor1 = new() { SeatNumber = 1 };

    [ObservableProperty]
    private Player _survivor2 = new() { SeatNumber = 2 };

    [ObservableProperty]
    private Player _survivor3 = new() { SeatNumber = 3 };

    [ObservableProperty]
    private Player _survivor4 = new() { SeatNumber = 4 };

    [ObservableProperty]
    private Player _hunter = new() { SeatNumber = 1 };
}
