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

public partial class TeamInfoPageViewModel : ViewModelBase
{
    private readonly BpApiClient _apiClient;
    private readonly BpRoomWorkspace _workspace;
    private string? _editingRoomId;

    public TeamInfoPageViewModel(BpApiClient apiClient, BpRoomWorkspace workspace)
    {
        _apiClient = apiClient;
        _workspace = workspace;
        MainTeam = new TeamEditorViewModel(this, "主队");
        AwayTeam = new TeamEditorViewModel(this, "客队");
        LoadFromRoom(_workspace.SelectedRoom);
    }

    public TeamEditorViewModel MainTeam { get; }

    public TeamEditorViewModel AwayTeam { get; }

    public string CurrentRoomTitle => _workspace.CurrentRoomTitle;

    public string StatusMessage => _workspace.StatusMessage;

    public bool HasSelectedRoom => _workspace.SelectedRoom is not null;

    private void LoadFromRoom(BpRoom? room)
    {
        _editingRoomId = room?.RoomId;
        MainTeam.LoadFrom(room?.TeamA, "主队");
        AwayTeam.LoadFrom(room?.TeamB, "客队");
        OnPropertyChanged(nameof(CurrentRoomTitle));
        OnPropertyChanged(nameof(HasSelectedRoom));
        OnPropertyChanged(nameof(StatusMessage));
    }

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

    public partial class TeamEditorViewModel : ObservableObject
    {
        private readonly TeamInfoPageViewModel _owner;

        public TeamEditorViewModel(TeamInfoPageViewModel owner, string title)
        {
            _owner = owner;
            Title = title;
        }

        public string Title { get; }

        [ObservableProperty]
        private string _teamName = string.Empty;

        [ObservableProperty]
        private Bitmap? _logoPreview;

        [ObservableProperty]
        private string _logoStatus = "未设置队标";

        private byte[]? _logoData;

        public ObservableCollection<TeamMemberEditItem> Members { get; } = [];

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

        [RelayCommand]
        private void ClearLogo() => SetLogoData(null);

        [RelayCommand]
        private void AddMember()
        {
            Members.Add(new TeamMemberEditItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"队员 {Members.Count + 1}"
            });
        }

        [RelayCommand]
        private void RemoveMember(TeamMemberEditItem? member)
        {
            if (member is not null)
            {
                Members.Remove(member);
            }
        }

        [RelayCommand]
        private Task SaveAsync() => _owner.SaveAsync();

        private void SetLogoData(byte[]? logoData)
        {
            _logoData = logoData is { Length: > 0 } ? logoData : null;
            LogoPreview = CreateBitmap(_logoData);
            LogoStatus = _logoData is null ? "未设置队标" : $"已设置队标 ({_logoData.Length / 1024D:0.#} KB)";
        }

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

public partial class TeamMemberEditItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;
}
