using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Models.Enums;

namespace Idvbp.Neo.Models;

public partial class Team : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _logoUrl;

    [ObservableProperty]
    private byte[]? _logoData;

    [ObservableProperty]
    private ObservableCollection<Player> _members = [];

    [ObservableProperty]
    private GameSide _currentSide;
}
