using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

/// <summary>
/// 角色选择模型，包含四名求生者与一名监管者选手。
/// </summary>
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
