namespace Idvbp.Neo.Models;

/// <summary>
/// 角色信息记录。
/// </summary>
public record CharacterInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
    public CharacterRole Role { get; init; }
}

/// <summary>
/// 角色阵营枚举。
/// </summary>
public enum CharacterRole
{
    Survivor,
    Hunter
}
