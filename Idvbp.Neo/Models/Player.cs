using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

public partial class Player : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _avatarUrl;

    [ObservableProperty]
    private string _teamId = string.Empty;

    [ObservableProperty]
    private int _seatNumber;

    [ObservableProperty]
    private string _characterId = string.Empty;
}
