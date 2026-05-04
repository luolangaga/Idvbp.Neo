using LiteDB;
using System.Text.Json.Serialization;

namespace Idvbp.Neo.Models;

/// <summary>
/// 地图信息记录，包含地图标识、名称、图片与禁用/可选状态。
/// </summary>
public record MapInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }

    [BsonIgnore]
    [JsonIgnore]
    public string? ImageSource
    {
        get => ImageUrl;
        set => ImageUrl = value;
    }

    [BsonIgnore]
    [JsonIgnore]
    public string MapName
    {
        get => Name;
        set => Name = value;
    }

    public bool IsBanned { get; set; }
    public bool CanBeBanned { get; set; } = true;
    public bool CanBePicked { get; set; } = true;
}
