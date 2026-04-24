using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

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
