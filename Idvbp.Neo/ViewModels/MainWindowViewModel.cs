using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Models.Enums;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Service;
using Idvbp.Neo.Views.Pages;
using NavigationViewItem = Idvbp.Neo.Controls.NavigationViewItem;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.Avalonia.Fluent.SymbolIconSource;

namespace Idvbp.Neo.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public BpRoomWorkspace Workspace { get; }

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

    public ObservableCollection<string> RecommendTimerList { get; } = ["20", "30", "45", "60"];

    public ObservableCollection<string> GameList { get; } = ["第 1 局", "第 2 局", "第 3 局", "第 4 局", "第 5 局"];

    [ObservableProperty]
    private string _selectedGameProgress = "第 1 局";

    [ObservableProperty]
    private string _actionName = "等待操作";

    [ObservableProperty]
    private string _roomName = "默认比赛";

    [ObservableProperty]
    private string _teamAName = "主队";

    [ObservableProperty]
    private string _teamBName = "客队";

    [ObservableProperty]
    private int _mapBanSlotsPerSide = 1;

    [ObservableProperty]
    private int _survivorBanSlots = 2;

    [ObservableProperty]
    private int _hunterBanSlots = 2;

    [ObservableProperty]
    private int _globalSurvivorBanSlots = 1;

    [ObservableProperty]
    private int _globalHunterBanSlots = 1;

    [ObservableProperty]
    private bool _resetGlobalBansOnNextMatch;
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
            Content = "Web反代",
            IconSource = new SymbolIconSource { Symbol = Symbol.Globe },
            TargetPageType = typeof(WebProxyPage)
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

    public MainWindowViewModel(BpRoomWorkspace workspace)
    {
        Workspace = workspace;
        NavigationSelectedItem = MenuItems[0];
        _ = Workspace.RefreshRoomsAsync();
    }

    [ObservableProperty]
    public partial NavigationViewItem NavigationSelectedItem { get; set; }

    [RelayCommand]
    private Task RefreshRoomsAsync()
        => Workspace.RefreshRoomsAsync();

    [RelayCommand]
    private Task NewGameAsync()
        => Workspace.CreateRoomAsync(new CreateRoomRequest
        {
            RoomName = string.IsNullOrWhiteSpace(RoomName) ? $"比赛 {DateTime.Now:yyyyMMdd-HHmmss}" : RoomName.Trim(),
            TeamAName = string.IsNullOrWhiteSpace(TeamAName) ? "主队" : TeamAName.Trim(),
            TeamBName = string.IsNullOrWhiteSpace(TeamBName) ? "客队" : TeamBName.Trim(),
            MapBanSlotsPerSide = MapBanSlotsPerSide,
            SurvivorBanSlots = SurvivorBanSlots,
            HunterBanSlots = HunterBanSlots,
            GlobalSurvivorBanSlots = GlobalSurvivorBanSlots,
            GlobalHunterBanSlots = GlobalHunterBanSlots
        });

    [RelayCommand]
    private Task NextGameAsync()
        => Workspace.CreateNextMatchAsync(BpPhase.GlobalBans, ResetGlobalBansOnNextMatch);

    [RelayCommand]
    private void Swap()
    {
        (TeamAName, TeamBName) = (TeamBName, TeamAName);
    }

    [RelayCommand]
    private void TimerStart()
    {
        RemainingSeconds = int.TryParse(TimerTime, out var seconds) ? Math.Max(0, seconds) : 0;
        Workspace.StatusMessage = $"倒计时已设置为 {RemainingSeconds} 秒。";
    }

    [RelayCommand]
    private void TimerStop()
    {
        RemainingSeconds = 0;
        Workspace.StatusMessage = "倒计时已停止。";
    }

    [RelayCommand]
    private void SaveGameInfo()
    {
        Workspace.StatusMessage = "比赛信息已在内置数据库中自动持久化。";
    }

    [RelayCommand]
    private void ImportGameInfo()
    {
        Workspace.StatusMessage = "导入比赛信息暂未接入文件格式，请使用新建比赛入口。";
    }

    [RelayCommand]
    private void StartNavigation()
    {
        IsGuidanceStarted = true;
        ActionName = "对局引导中";
    }

    [RelayCommand]
    private void NavigateToPreviousStep()
    {
        ActionName = "已切到上一步";
    }

    [RelayCommand]
    private void NavigateToNextStep()
    {
        ActionName = "已切到下一步";
    }

    [RelayCommand]
    private void StopNavigation()
    {
        IsGuidanceStarted = false;
        ActionName = "引导已结束";
    }
}
