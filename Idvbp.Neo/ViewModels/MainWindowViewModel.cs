using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.ViewModels.Pages;

namespace Idvbp.Neo.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private string _surTeamName = "求生者队伍";

    [ObservableProperty]
    private string _hunTeamName = "监管者队伍";

    [ObservableProperty]
    private int _remainingSeconds;

    [ObservableProperty]
    private string _timerTime = "30";

    [ObservableProperty]
    private bool _isGuidanceStarted;

    public ObservableCollection<NavigationItem> MenuItems { get; } = [];
    public ObservableCollection<NavigationItem> FooterMenuItems { get; } = [];

    public MainWindowViewModel()
    {
        MenuItems.Add(new NavigationItem("Home", "Home", new HomePageViewModel()));
        MenuItems.Add(new NavigationItem("选角色", "Pick", new PickPageViewModel()));
        MenuItems.Add(new NavigationItem("Ban求生者", "BanSur", new BanSurPageViewModel()));
        MenuItems.Add(new NavigationItem("Ban监管者", "BanHun", new BanHunPageViewModel()));
        MenuItems.Add(new NavigationItem("天赋", "Talent", new TalentPageViewModel()));
        MenuItems.Add(new NavigationItem("计分", "Score", new ScorePageViewModel()));
        MenuItems.Add(new NavigationItem("赛后数据", "GameData", new GameDataPageViewModel()));
        MenuItems.Add(new NavigationItem("地图BP", "MapBp", new MapBpPageViewModel()));
        MenuItems.Add(new NavigationItem("队伍信息", "TeamInfo", new TeamInfoPageViewModel()));

        FooterMenuItems.Add(new NavigationItem("SmartBP", "SmartBP", new SmartBpPageViewModel()));
        FooterMenuItems.Add(new NavigationItem("前端管理", "FrontManage", new FrontManagePageViewModel()));
        FooterMenuItems.Add(new NavigationItem("插件", "Plugin", new PluginPageViewModel()));
        FooterMenuItems.Add(new NavigationItem("设置", "Setting", new SettingPageViewModel()));

        CurrentPage = MenuItems[0].PageVm;
    }

    [RelayCommand]
    private void Navigate(NavigationItem item)
    {
        CurrentPage = item.PageVm;
    }

    public sealed record NavigationItem(string Title, string IconKey, ViewModelBase PageVm);
}
