using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

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

public record GameDataPlayer(string CharacterName, string Data = "");
