namespace Idvbp.Neo.Models;

/// <summary>
/// 队伍选择信息记录。
/// </summary>
public record TeamSelectInfo
{
    public string TeamType { get; set; } = "";
    public string TeamName { get; set; } = "";
}
