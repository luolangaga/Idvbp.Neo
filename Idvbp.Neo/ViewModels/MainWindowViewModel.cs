using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Views.Pages;
using NavigationViewItem = Idvbp.Neo.Controls.NavigationViewItem;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.Avalonia.Fluent.SymbolIconSource;

namespace Idvbp.Neo.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }

    [ObservableProperty]
    public partial bool IsPaneOpen { get; set; } = true;

    [ObservableProperty]
    public partial string SurTeamName { get; set; } = "求生者队伍";

    [ObservableProperty]
    public partial string HunTeamName { get; set; } = "监管者队伍";

    [ObservableProperty]
    public partial int RemainingSeconds { get; set; }

    [ObservableProperty]
    public partial string TimerTime { get; set; } = "30";

    [ObservableProperty]
    public partial bool IsGuidanceStarted { get; set; }
    public ObservableCollection<NavigationViewItem> MenuItems { get; } =
    [
        new()
        {
            Content = "启动页",
            IconSource = new SymbolIconSource { Symbol = Symbol.Home },
            TargetPageType = typeof(HomePage)
        },
        new()
        {
            Content = "队伍信息",
            IconSource = new SymbolIconSource { Symbol = Symbol.People },
            TargetPageType = typeof(TeamInfo)
        },
        new()
        {
            Content = "地图BP",
            IconSource = new SymbolIconSource { Symbol = Symbol.Map },
            TargetPageType = typeof(MapBp)
        },
        new()
        {
            Content = "Ban监管",
            IconSource = new SymbolIconSource { Symbol = Symbol.PresenterOff },
            TargetPageType = typeof(BanHunPage)
        },
        new()
        {
            Content = "Ban求生",
            IconSource = new SymbolIconSource { Symbol = Symbol.PersonProhibited },
            TargetPageType = typeof(BanSurPage)
        },
        new()
        {
            Content = "角色选择",
            IconSource = new SymbolIconSource { Symbol = Symbol.PersonAdd },
            TargetPageType = typeof(PickPage)
        },
        new()
        {
            Content = "天赋特质",
            IconSource = new SymbolIconSource { Symbol = Symbol.PersonWalking },
            TargetPageType = typeof(TalentPage)
        },
        new()
        {
            Content = "比分控制",
            IconSource = new SymbolIconSource { Symbol = Symbol.NumberRow },
            TargetPageType = typeof(ScorePage)
        },
        new()
        {
            Content = "赛后数据",
            IconSource = new SymbolIconSource { Symbol = Symbol.TextBulletListSquare },
            TargetPageType = typeof(GameDataPage)
        }
    ];

    public ObservableCollection<NavigationViewItem> FooterMenuItems { get; } =
    [
        new()
        {
            Content = "智慧BP",
            IconSource = new SymbolIconSource { Symbol = Symbol.ScanText },
            TargetPageType = typeof(SmartBpPage)
        },
        new()
        {
            Content = "插件管理",
            IconSource = new SymbolIconSource { Symbol = Symbol.AppsAddIn },
            TargetPageType = typeof(PluginPage)
        },
        new()
        {
            Content = "前台管理",
            IconSource = new SymbolIconSource { Symbol = Symbol.ShareScreenStart },
            TargetPageType = typeof(FrontManagePage)
        },
        new()
        {
            Content = "设置",
            IconSource = new SymbolIconSource { Symbol = Symbol.Settings },
            TargetPageType = typeof(SettingPage)
        }
    ];

    public MainWindowViewModel()
    {
        NavigationSelectedItem = MenuItems[0];
    }

    [ObservableProperty]
    public partial NavigationViewItem NavigationSelectedItem { get; set; }
}
