using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Models;

namespace Idvbp.Neo.ViewModels.Pages;

public partial class TeamInfoPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _mainTeamName = "主队";

    [ObservableProperty]
    private string? _mainTeamLogoUrl;

    [ObservableProperty]
    private string _mainTeamCurrentSide = "求生者";

    [ObservableProperty]
    private ObservableCollection<PlayerInfo> _mainSurMembers = [];

    [ObservableProperty]
    private ObservableCollection<PlayerInfo> _mainHunMembers = [];

    [ObservableProperty]
    private string _awayTeamName = "客队";

    [ObservableProperty]
    private string? _awayTeamLogoUrl;

    [ObservableProperty]
    private string _awayTeamCurrentSide = "监管者";

    [ObservableProperty]
    private ObservableCollection<PlayerInfo> _awaySurMembers = [];

    [ObservableProperty]
    private ObservableCollection<PlayerInfo> _awayHunMembers = [];

    [ObservableProperty]
    private ObservableCollection<PlayerInfo> _onFieldSurPlayerNames = [];

    [ObservableProperty]
    private string _onFieldHunPlayerName = "";
}
