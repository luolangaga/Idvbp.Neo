using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 游戏数据页面视图模型。
/// </summary>
public partial class GameDataPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<GameDataPlayer> _surPlayerList = new()
    {
        new GameDataPlayer("求生者1"),
        new GameDataPlayer("求生者2"),
        new GameDataPlayer("求生者3"),
        new GameDataPlayer("求生者4"),
    };

    [ObservableProperty]
    private GameDataPlayer _hunPlayer = new("监管者");
}

/// <summary>
/// 游戏数据玩家记录。
/// </summary>
public record GameDataPlayer(string CharacterName, string Data = "");
