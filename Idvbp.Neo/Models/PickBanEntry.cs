using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

public partial class PickBanEntry : ObservableObject
{
    [ObservableProperty]
    private string _characterId = string.Empty;

    [ObservableProperty]
    private int _order;

    public PickBanEntry() { }

    public PickBanEntry(string characterId, int order)
    {
        _characterId = characterId;
        _order = order;
    }
}
