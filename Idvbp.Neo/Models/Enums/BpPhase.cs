namespace Idvbp.Neo.Models.Enums;

/// <summary>
/// BP 阶段枚举，定义比赛各阶段状态。
/// </summary>
public enum BpPhase
{
    Waiting,
    GlobalBans,
    MapBan,
    MapPick,
    SideBans,
    CharacterPicks,
    Ready,
    InProgress,
    Finished
}
