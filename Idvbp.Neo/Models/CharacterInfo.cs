namespace Idvbp.Neo.Models;

public record CharacterInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
    public CharacterRole Role { get; init; }
}

public enum CharacterRole
{
    Survivor,
    Hunter
}
