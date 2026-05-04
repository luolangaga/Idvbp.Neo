using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

/// <summary>
/// 禁用/选择条目模型，包含角色 ID 与顺序。
/// </summary>
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
