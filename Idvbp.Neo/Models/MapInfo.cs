namespace Idvbp.Neo.Models;

public record MapInfo
{
    public string? ImageSource { get; set; }
    public string MapName { get; set; } = "";
    public bool IsBanned { get; set; }
    public bool CanBeBanned { get; set; } = true;
    public bool CanBePicked { get; set; } = true;
}
