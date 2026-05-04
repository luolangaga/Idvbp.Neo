using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 天赋页面视图模型。
/// </summary>
public partial class TalentPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<TalentPlayer> _surPlayerList = new()
    {
        new TalentPlayer("求生者1"),
        new TalentPlayer("求生者2"),
        new TalentPlayer("求生者3"),
        new TalentPlayer("求生者4"),
    };

    [ObservableProperty]
    private TalentPlayer _hunPlayer = new("监管者");

    [ObservableProperty]
    private bool _isSurTalentHighlighted;

    [ObservableProperty]
    private bool _isHunTalentHighlighted;

    [ObservableProperty]
    private bool _isTraitVisible = true;

    [ObservableProperty]
    private int _selectedTrait;
}

/// <summary>
/// 天赋玩家记录。
/// </summary>
public record TalentPlayer(string CharacterName);
