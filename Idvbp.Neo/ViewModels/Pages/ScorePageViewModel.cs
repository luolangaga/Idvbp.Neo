using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 比分页面视图模型。
/// </summary>
public partial class ScorePageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _homeTeamName = "主队";

    [ObservableProperty]
    private string _awayTeamName = "客队";

    [ObservableProperty]
    private int _homeWinCount;

    [ObservableProperty]
    private int _homeTieCount;

    [ObservableProperty]
    private string _homeGameScores = "0";

    [ObservableProperty]
    private int _awayWinCount;

    [ObservableProperty]
    private int _awayTieCount;

    [ObservableProperty]
    private string _awayGameScores = "0";

    [ObservableProperty]
    private bool _isGameFinished;

    [ObservableProperty]
    private int _homeTeamCamp;

    [ObservableProperty]
    private int _selectedGameResult;

    [ObservableProperty]
    private object? _selectedGameProgress;

    [ObservableProperty]
    private ObservableCollection<GameListItem> _gameList = new();

    [ObservableProperty]
    private bool _isDebugContentVisible;
}

public record GameListItem(string Key, string Value);
