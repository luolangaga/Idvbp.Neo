using System;
using System.Collections.Generic;

namespace Idvbp.Neo.Server.Resources;

/// <summary>
/// 资源图片元数据模型。
/// </summary>
public sealed class ResourceImageMetadata
{
    public string Variant { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset LastModifiedUtc { get; init; }
    public bool IsPrimary { get; init; }
}

/// <summary>
/// 角色资源项模型。
/// </summary>
public sealed class CharacterResourceItem
{
    public string Id { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string ImageFileName { get; init; } = string.Empty;
    public string? Abbrev { get; init; }
    public string? FullSpell { get; init; }
    public IReadOnlyDictionary<string, string?> Names { get; init; } = new Dictionary<string, string?>();
    public IReadOnlyList<ResourceImageMetadata> Images { get; init; } = Array.Empty<ResourceImageMetadata>();
}

/// <summary>
/// 地图资源项模型。
/// </summary>
public sealed class MapResourceItem
{
    public string Id { get; init; } = string.Empty;
    public string AssetKey { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string?> Names { get; init; } = new Dictionary<string, string?>();
    public IReadOnlyList<ResourceImageMetadata> Images { get; init; } = Array.Empty<ResourceImageMetadata>();
}

/// <summary>
/// 角色本地化条目内部模型（兼容旧数据格式）。
/// </summary>
internal sealed class LegacyCharacterLocaleEntry
{
    public string Id { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string ImageFileName { get; init; } = string.Empty;
    public string? Abbrev { get; init; }
    public string? FullSpell { get; init; }
    public Dictionary<string, string?> Names { get; init; } = new();
}

/// <summary>
/// 地图目录条目内部模型。
/// </summary>
internal sealed class MapCatalogEntry
{
    public string Id { get; init; } = string.Empty;
    public string AssetKey { get; init; } = string.Empty;
    public Dictionary<string, string?> Names { get; init; } = new();
}
