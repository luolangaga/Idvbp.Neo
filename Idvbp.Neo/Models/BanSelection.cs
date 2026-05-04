using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

/// <summary>
/// 单局禁用选择模型，包含求生者与监管者的禁用列表及槽位数。
/// </summary>
public partial class BanSelection : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<PickBanEntry> _survivorBans = [];

    [ObservableProperty]
    private ObservableCollection<PickBanEntry> _hunterBans = [];

    [ObservableProperty]
    private int _survivorBanSlots;

    [ObservableProperty]
    private int _hunterBanSlots;
}
