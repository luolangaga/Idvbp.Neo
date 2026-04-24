namespace Idvbp.Neo.Models;

public record MapInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
}
