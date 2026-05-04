using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.Models;

/// <summary>
/// 选手模型，包含选手基本信息、所属队伍与角色绑定。
/// </summary>
public partial class Player : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _avatarUrl;

    [ObservableProperty]
    private string _teamId = string.Empty;

    [ObservableProperty]
    private int _seatNumber;

    [ObservableProperty]
    private string _characterId = string.Empty;
}
