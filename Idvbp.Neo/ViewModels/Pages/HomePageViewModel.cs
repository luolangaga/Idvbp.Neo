using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Models;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Service;
using Idvbp.Neo.Services;

namespace Idvbp.Neo.ViewModels.Pages;

public partial class HomePageViewModel : ViewModelBase
{
    private readonly BpRoomWorkspace _workspace;
    private readonly AppNotificationService _notifications;

    public HomePageViewModel(BpRoomWorkspace workspace, AppNotificationService notifications)
    {
        _workspace = workspace;
        _notifications = notifications;
    }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _newRoomName = string.Empty;

    [ObservableProperty]
    private string _newTeamAName = "主队";

    [ObservableProperty]
    private string _newTeamBName = "客队";

    public ObservableCollection<BpRoom> Rooms => _workspace.Rooms;

    [RelayCommand]
    private async Task LoadAllRoomsAsync()
    {
        await _workspace.RefreshRoomsAsync();
    }

    public async Task SwitchToRoomAsync(string? roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return;
        await _workspace.SwitchRoomAsync(roomId);
    }

    public async Task DeleteRoomAsync(string? roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return;
        await _workspace.DeleteRoomAsync(roomId);
        await _workspace.RefreshRoomsAsync();
    }

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        var room = await _workspace.CreateRoomAsync(new CreateRoomRequest
        {
            RoomName = string.IsNullOrWhiteSpace(NewRoomName) ? "默认比赛" : NewRoomName.Trim(),
            TeamAName = string.IsNullOrWhiteSpace(NewTeamAName) ? "主队" : NewTeamAName.Trim(),
            TeamBName = string.IsNullOrWhiteSpace(NewTeamBName) ? "客队" : NewTeamBName.Trim()
        });

        if (room is not null)
        {
            NewRoomName = string.Empty;
            await _workspace.RefreshRoomsAsync();
        }
    }

    [ObservableProperty]
    private string _releaseNotes = """
        # 更新日志

        ## v1.0.0 (2026-04-24)

        ### 新增功能
        - 全新的现代化 UI 界面
        - 支持地图 BP 功能
        - 支持队伍信息管理
        - 支持 JSON 导入队伍数据

        ### 优化
        - 优化界面交互体验
        - 提升应用启动速度

        ### 修复
        - 修复若干已知问题

        ---

        更多更新内容请关注官方文档。
        """;
}
