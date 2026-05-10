using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Models;
using Idvbp.Neo.Models.Enums;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Service;
using Idvbp.Neo.Services;
using Idvbp.Neo.Views.Pages;
using NavigationViewItem = Idvbp.Neo.Controls.NavigationViewItem;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.Avalonia.Fluent.SymbolIconSource;

namespace Idvbp.Neo.ViewModels;

/// <summary>
/// 主窗口视图模型，管理导航菜单、比赛配置、计时器及房间操作命令。
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private const string DefaultRoomNamePrefix = "默认比赛";
    private bool _suppressGameProgressSync;

    /// <summary>
    /// 房间工作区实例，用于管理房间数据与实时同步。
    /// </summary>
    public BpRoomWorkspace Workspace { get; }

    public AppNotificationService Notifications { get; }

    /// <summary>
    /// 当前显示的页面视图模型。
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }

    /// <summary>
    /// 导航窗格是否展开。
    /// </summary>
    [ObservableProperty]
    public partial bool IsPaneOpen { get; set; } = true;

    /// <summary>
    /// 求生者队伍名称。
    /// </summary>
    [ObservableProperty]
    public partial string SurTeamName { get; set; } = "求生者队伍";

    /// <summary>
    /// 监管者队伍名称。
    /// </summary>
    [ObservableProperty]
    public partial string HunTeamName { get; set; } = "监管者队伍";

    /// <summary>
    /// 倒计时剩余秒数。
    /// </summary>
    [ObservableProperty]
    public partial int RemainingSeconds { get; set; }

    /// <summary>
    /// 计时器设定时间（秒）。
    /// </summary>
    [ObservableProperty]
    public partial string TimerTime { get; set; } = "30";

    /// <summary>
    /// 是否已启动对局引导。
    /// </summary>
    [ObservableProperty]
    public partial bool IsGuidanceStarted { get; set; }

    /// <summary>
    /// 推荐计时器时长列表。
    /// </summary>
    public ObservableCollection<string> RecommendTimerList { get; } = ["20", "30", "45", "60"];

    /// <summary>
    /// 局数列表。
    /// </summary>
    public ObservableCollection<string> GameList { get; } = ["第 1 局", "第 2 局", "第 3 局", "第 4 局", "第 5 局"];

    /// <summary>
    /// 当前选中的局数进度。
    /// </summary>
    [ObservableProperty]
    private string _selectedGameProgress = "第 1 局";

    partial void OnSelectedGameProgressChanged(string value)
    {
        if (_suppressGameProgressSync)
        {
            return;
        }

        if (!TryParseGameRound(value, out var targetRound))
        {
            return;
        }

        _ = SyncGameProgressAsync(targetRound);
    }

    /// <summary>
    /// 当前操作状态名称。
    /// </summary>
    [ObservableProperty]
    private string _actionName = "等待操作";

    /// <summary>
    /// 房间名称。
    /// </summary>
    [ObservableProperty]
    private string _roomName = "默认比赛";

    /// <summary>
    /// A 队（主队）名称。
    /// </summary>
    [ObservableProperty]
    private string _teamAName = "主队";

    /// <summary>
    /// B 队（客队）名称。
    /// </summary>
    [ObservableProperty]
    private string _teamBName = "客队";

    /// <summary>
    /// 下一局比赛是否重置全局禁用。
    /// </summary>
    [ObservableProperty]
    private bool _resetGlobalBansOnNextMatch;

    /// <summary>
    /// 主导航菜单项集合。
    /// </summary>
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

    /// <summary>
    /// 底部导航菜单项集合（功能与管理类页面）。
    /// </summary>
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
            Content = "页面管理",
            IconSource = new SymbolIconSource { Symbol = Symbol.ShareScreenStart },
            TargetPageType = typeof(WebProxyPage)
        },
        new()
        {
            Content = "设置",
            IconSource = new SymbolIconSource { Symbol = Symbol.Settings },
            TargetPageType = typeof(SettingPage)
        }
    ];

    /// <summary>
    /// 初始化主窗口视图模型，并自动刷新房间列表。
    /// </summary>
    /// <param name="workspace">房间工作区实例。</param>
    public MainWindowViewModel(BpRoomWorkspace workspace, AppNotificationService notifications)
    {
        Workspace = workspace;
        Notifications = notifications;
        NavigationSelectedItem = MenuItems[0];
        Workspace.ActiveRoomChanged += OnActiveRoomChanged;
        _ = Workspace.RefreshRecentRoomsAsync();
    }

    /// <summary>
    /// 当前选中的导航项。
    /// </summary>
    [ObservableProperty]
    public partial NavigationViewItem NavigationSelectedItem { get; set; }

    /// <summary>
    /// 刷新房间列表命令。
    /// </summary>
    [RelayCommand]
    private Task RefreshRoomsAsync()
        => Workspace.RefreshRecentRoomsAsync();

    /// <summary>
    /// 新建比赛命令，根据当前配置创建房间。
    /// </summary>
    [RelayCommand]
    private Task NewGameAsync()
        => Workspace.CreateRoomAsync(new CreateRoomRequest
        {
            RoomName = ResolveNewRoomName(),
            TeamAName = string.IsNullOrWhiteSpace(TeamAName) ? "主队" : TeamAName.Trim(),
            TeamBName = string.IsNullOrWhiteSpace(TeamBName) ? "客队" : TeamBName.Trim()
        });

    /// <summary>
    /// 下一局命令，创建新对局并可选重置全局禁用。
    /// </summary>
    [RelayCommand]
    private Task NextGameAsync()
        => Workspace.CreateNextMatchAsync(BpPhase.GlobalBans, ResetGlobalBansOnNextMatch);

    private async Task SyncGameProgressAsync(int targetRound)
    {
        var room = await Workspace.AdvanceToRoundAsync(targetRound, BpPhase.GlobalBans, ResetGlobalBansOnNextMatch);
        if (room is not null)
        {
            SetSelectedGameProgressSilently(room.CurrentRound);
        }
        else if (Workspace.SelectedRoom is not null)
        {
            SetSelectedGameProgressSilently(Workspace.SelectedRoom.CurrentRound);
        }
    }

    private void OnActiveRoomChanged(BpRoom? room)
    {
        if (room is not null)
        {
            SetSelectedGameProgressSilently(room.CurrentRound);
        }
    }

    private void SetSelectedGameProgressSilently(int round)
    {
        _suppressGameProgressSync = true;
        SelectedGameProgress = $"第 {round} 局";
        _suppressGameProgressSync = false;
    }

    private string ResolveNewRoomName()
    {
        if (!string.IsNullOrWhiteSpace(RoomName)
            && !RoomName.Trim().StartsWith(DefaultRoomNamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return RoomName.Trim();
        }

        var nextNumber = Workspace.Rooms
            .Select(x => TryGetDefaultRoomNumber(x.RoomName, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"{DefaultRoomNamePrefix} {nextNumber}";
    }

    private static bool TryGetDefaultRoomNumber(string? roomName, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(roomName))
        {
            return false;
        }

        var value = roomName.Trim();
        if (!value.StartsWith(DefaultRoomNamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = value[DefaultRoomNamePrefix.Length..].Trim();
        return int.TryParse(suffix, out number);
    }

    private static bool TryParseGameRound(string? value, out int round)
    {
        round = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out round) && round > 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing || IsDisposed)
        {
            return;
        }

        Workspace.ActiveRoomChanged -= OnActiveRoomChanged;

        base.Dispose(disposing);
    }

    /// <summary>
    /// 交换双方队伍名称命令。
    /// </summary>
    [RelayCommand]
    private void Swap()
    {
        (TeamAName, TeamBName) = (TeamBName, TeamAName);
    }

    /// <summary>
    /// 启动倒计时命令。
    /// </summary>
    [RelayCommand]
    private void TimerStart()
    {
        RemainingSeconds = int.TryParse(TimerTime, out var seconds) ? Math.Max(0, seconds) : 0;
        Workspace.StatusMessage = $"倒计时已设置为 {RemainingSeconds} 秒。";
        Notifications.Info($"倒计时已设置为 {RemainingSeconds} 秒。");
    }

    /// <summary>
    /// 停止倒计时命令。
    /// </summary>
    [RelayCommand]
    private void TimerStop()
    {
        RemainingSeconds = 0;
        Workspace.StatusMessage = "倒计时已停止。";
        Notifications.Info("倒计时已停止。");
    }

    /// <summary>
    /// 保存比赛信息命令。
    /// </summary>
    [RelayCommand]
    private void SaveGameInfo()
    {
        Workspace.StatusMessage = "比赛信息已在内置数据库中自动持久化。";
        Notifications.Success("比赛信息已在内置数据库中自动持久化。");
    }

    /// <summary>
    /// 导入比赛信息命令。
    /// </summary>
    [RelayCommand]
    private void ImportGameInfo()
    {
        Workspace.StatusMessage = "导入比赛信息暂未接入文件格式，请使用新建比赛入口。";
        Notifications.Warning("导入比赛信息暂未接入文件格式，请使用新建比赛入口。");
    }

    /// <summary>
    /// 开始对局引导命令。
    /// </summary>
    [RelayCommand]
    private void StartNavigation()
    {
        IsGuidanceStarted = true;
        ActionName = "对局引导中";
    }

    /// <summary>
    /// 导航到上一步命令。
    /// </summary>
    [RelayCommand]
    private void NavigateToPreviousStep()
    {
        ActionName = "已切到上一步";
    }

    /// <summary>
    /// 导航到下一步命令。
    /// </summary>
    [RelayCommand]
    private void NavigateToNextStep()
    {
        ActionName = "已切到下一步";
    }

    /// <summary>
    /// 停止对局引导命令。
    /// </summary>
    [RelayCommand]
    private void StopNavigation()
    {
        IsGuidanceStarted = false;
        ActionName = "引导已结束";
    }
}
