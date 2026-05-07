using System.Text.Json.Serialization;

namespace Idvbp.Neo.Models.Enums;

/// <summary>
/// 游戏阵营枚举。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameSide
{
    Survivor,
    Hunter
}
