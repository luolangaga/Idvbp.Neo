using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Client;
using Idvbp.Neo.Models;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Service;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 队伍信息页面视图模型。
/// </summary>
public partial class TeamInfoPageViewModel : ViewModelBase
{
    private readonly BpApiClient _apiClient;
    private readonly BpRoomWorkspace _workspace;
    private string? _editingRoomId;

    /// <summary>
    /// 初始化队伍信息页面视图模型。
    /// </summary>
    public TeamInfoPageViewModel(BpApiClient apiClient, BpRoomWorkspace workspace)
    {
        _apiClient = apiClient;
        _workspace = workspace;
        MainTeam = new TeamEditorViewModel(this, "主队");
        AwayTeam = new TeamEditorViewModel(this, "客队");
        _workspace.ActiveRoomChanged += LoadFromRoom;
        _workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(BpRoomWorkspace.StatusMessage) or nameof(BpRoomWorkspace.SelectedRoom))
            {
                OnPropertyChanged(nameof(CurrentRoomTitle));
                OnPropertyChanged(nameof(HasSelectedRoom));
                OnPropertyChanged(nameof(StatusMessage));
            }
        };
        LoadFromRoom(_workspace.SelectedRoom);
    }

    public TeamEditorViewModel MainTeam { get; }

    public TeamEditorViewModel AwayTeam { get; }

    /// <summary>
    /// 当前房间标题。
    /// </summary>
    public string CurrentRoomTitle => _workspace.CurrentRoomTitle;

    /// <summary>
    /// 状态消息。
    /// </summary>
    public string StatusMessage => _workspace.StatusMessage;

    /// <summary>
    /// 是否已选择房间。
    /// </summary>
    public bool HasSelectedRoom => _workspace.SelectedRoom is not null;

    /// <summary>
    /// 从房间加载数据。
    /// </summary>
    private void LoadFromRoom(BpRoom? room)
    {
        _editingRoomId = room?.RoomId;
        MainTeam.LoadFrom(room?.TeamA, "主队");
        AwayTeam.LoadFrom(room?.TeamB, "客队");
        OnPropertyChanged(nameof(CurrentRoomTitle));
        OnPropertyChanged(nameof(HasSelectedRoom));
        OnPropertyChanged(nameof(StatusMessage));
    }

    /// <summary>
    /// 保存队伍信息。
    /// </summary>
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_editingRoomId))
        {
            _workspace.StatusMessage = "请先选择或新建比赛房间。";
            OnPropertyChanged(nameof(StatusMessage));
            return;
        }

        BpRoom room;
        try
        {
            room = await _apiClient.UpdateTeamsAsync(_editingRoomId, new UpdateRoomTeamsRequest
            {
                TeamA = MainTeam.ToRequest(),
                TeamB = AwayTeam.ToRequest()
            });
        }
        catch (Exception ex)
        {
            _workspace.StatusMessage = $"保存队伍信息失败: {ex.Message}";
            OnPropertyChanged(nameof(StatusMessage));
            return;
        }

        _workspace.AcceptServerRoom(room);
        _workspace.StatusMessage = "队伍信息已保存。";
        LoadFromRoom(room);
    }

    /// <summary>
    /// 队伍编辑器视图模型。
    /// </summary>
    public partial class TeamEditorViewModel : ObservableObject
    {
        private readonly TeamInfoPageViewModel _owner;

        /// <summary>
        /// 初始化队伍编辑器视图模型。
        /// </summary>
        public TeamEditorViewModel(TeamInfoPageViewModel owner, string title)
        {
            _owner = owner;
            Title = title;
        }

        /// <summary>
        /// 标题。
        /// </summary>
        public string Title { get; }

        [ObservableProperty]
        private string _teamName = string.Empty;

        [ObservableProperty]
        private Bitmap? _logoPreview;

        [ObservableProperty]
        private string _logoStatus = "未设置队标";

        private byte[]? _logoData;

        /// <summary>
        /// 成员列表。
        /// </summary>
        public ObservableCollection<TeamMemberEditItem> Members { get; } = [];

        /// <summary>
        /// 从队伍加载数据。
        /// </summary>
        public void LoadFrom(Team? team, string fallbackName)
        {
            TeamName = string.IsNullOrWhiteSpace(team?.Name) ? fallbackName : team.Name;
            SetLogoData(team?.LogoData);

            Members.Clear();
            if (team?.Members is null)
            {
                return;
            }

            foreach (var member in team.Members)
            {
                Members.Add(new TeamMemberEditItem
                {
                    Id = member.Id,
                    Name = member.Name
                });
            }
        }

        /// <summary>
        /// 转换为请求对象。
        /// </summary>
        public UpdateTeamRequest ToRequest() => new()
        {
            Name = TeamName,
            LogoData = _logoData,
            Members = Members.Select(x => new UpdateTeamPlayerRequest
            {
                Id = x.Id,
                Name = x.Name
            }).ToArray()
        };

        /// <summary>
        /// 设置队标命令。
        /// </summary>
        [RelayCommand]
        private async Task SetLogoAsync()
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
            {
                _owner._workspace.StatusMessage = "当前环境无法打开文件选择器。";
                return;
            }

            var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择队伍 Logo",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("图片文件")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"]
                    }
                ]
            });

            var file = files.FirstOrDefault();
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            SetLogoData(memory.ToArray());
        }

        /// <summary>
        /// 清除队标命令。
        /// </summary>
        [RelayCommand]
        private void ClearLogo() => SetLogoData(null);

        /// <summary>
        /// 添加成员命令。
        /// </summary>
        [RelayCommand]
        private void AddMember()
        {
            Members.Add(new TeamMemberEditItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"队员 {Members.Count + 1}"
            });
        }

        /// <summary>
        /// 移除成员命令。
        /// </summary>
        [RelayCommand]
        private void RemoveMember(TeamMemberEditItem? member)
        {
            if (member is not null)
            {
                Members.Remove(member);
            }
        }

        /// <summary>
        /// 保存命令。
        /// </summary>
        [RelayCommand]
        private Task SaveAsync() => _owner.SaveAsync();

        /// <summary>
        /// 设置队标数据。
        /// </summary>
        private void SetLogoData(byte[]? logoData)
        {
            _logoData = logoData is { Length: > 0 } ? logoData : null;
            LogoPreview = CreateBitmap(_logoData);
            LogoStatus = _logoData is null ? "未设置队标" : $"已设置队标 ({_logoData.Length / 1024D:0.#} KB)";
        }

        /// <summary>
        /// 从字节数组创建位图。
        /// </summary>
        private static Bitmap? CreateBitmap(byte[]? bytes)
        {
            if (bytes is not { Length: > 0 })
            {
                return null;
            }

            try
            {
                return new Bitmap(new MemoryStream(bytes));
            }
            catch
            {
                return null;
            }
        }
    }
}

/// <summary>
/// 队伍成员编辑项。
/// </summary>
public partial class TeamMemberEditItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;
}
