using System;
using System.Collections.Generic;

namespace Idvbp.Neo.Server.Resources;

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

public sealed class MapResourceItem
{
    public string Id { get; init; } = string.Empty;
    public string AssetKey { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string?> Names { get; init; } = new Dictionary<string, string?>();
    public IReadOnlyList<ResourceImageMetadata> Images { get; init; } = Array.Empty<ResourceImageMetadata>();
}

internal sealed class LegacyCharacterLocaleEntry
{
    public string Id { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string ImageFileName { get; init; } = string.Empty;
    public string? Abbrev { get; init; }
    public string? FullSpell { get; init; }
    public Dictionary<string, string?> Names { get; init; } = new();
}

internal sealed class MapCatalogEntry
{
    public string Id { get; init; } = string.Empty;
    public string AssetKey { get; init; } = string.Empty;
    public Dictionary<string, string?> Names { get; init; } = new();
}
