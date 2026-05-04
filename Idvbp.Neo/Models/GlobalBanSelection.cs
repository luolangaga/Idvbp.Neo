using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

/// <summary>
/// 全局禁用选择模型，包含跨局生效的求生者与监管者禁用列表及槽位数。
/// </summary>
public partial class GlobalBanSelection : ObservableObject
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
