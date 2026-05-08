using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Models.Enums;

namespace Idvbp.Neo.Models;

/// <summary>
/// 单局 BP 状态。房间保留通用信息，每一局独立保存地图、Ban、选角和阶段。
/// </summary>
public partial class RoundBpState : ObservableObject
{
    [ObservableProperty]
    private int _roundNumber = 1;

    [ObservableProperty]
    private BpPhase _phase = BpPhase.Waiting;

    [ObservableProperty]
    private GameSide _teamASide = GameSide.Survivor;

    [ObservableProperty]
    private GameSide _teamBSide = GameSide.Hunter;

    [ObservableProperty]
    private MapSelection _mapSelection = new();

    [ObservableProperty]
    private CharacterPickSelection _characterPicks = new();

    [ObservableProperty]
    private BanSelection _bans = new();

    [ObservableProperty]
    private GlobalBanSelection _globalBans = new();
}
