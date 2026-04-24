using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;

namespace Idvbp.Neo.Server.Resources;

public interface IResourceCatalogService
{
    IReadOnlyCollection<CharacterResourceItem> GetCharacters();
    CharacterResourceItem? GetCharacter(string id);
    IReadOnlyCollection<ResourceImageMetadata> GetCharacterImages(string id, IEnumerable<string>? variants);
    IReadOnlyCollection<MapResourceItem> GetMaps();
    MapResourceItem? GetMap(string id);
    IReadOnlyCollection<ResourceImageMetadata> GetMapImages(string id, IEnumerable<string>? variants);
}

public sealed class ResourceCatalogService : IResourceCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();
    private readonly string _resourcesRootPath;
    private readonly string _dataRootPath;
    private readonly IReadOnlyCollection<CharacterResourceItem> _characters;
    private readonly IReadOnlyCollection<MapResourceItem> _maps;

    public ResourceCatalogService(string resourcesRootPath)
    {
        _resourcesRootPath = resourcesRootPath;
        _dataRootPath = Path.Combine(resourcesRootPath, "data");
        _contentTypeProvider.Mappings[".webp"] = "image/webp";
        _characters = LoadCharacters();
        _maps = LoadMaps();
    }

    public IReadOnlyCollection<CharacterResourceItem> GetCharacters() => _characters;

    public CharacterResourceItem? GetCharacter(string id)
        => _characters.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyCollection<ResourceImageMetadata> GetCharacterImages(string id, IEnumerable<string>? variants)
        => FilterImages(GetCharacter(id)?.Images, variants);

    public IReadOnlyCollection<MapResourceItem> GetMaps() => _maps;

    public MapResourceItem? GetMap(string id)
        => _maps.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyCollection<ResourceImageMetadata> GetMapImages(string id, IEnumerable<string>? variants)
        => FilterImages(GetMap(id)?.Images, variants);

    private IReadOnlyCollection<CharacterResourceItem> LoadCharacters()
    {
        var filePath = Path.Combine(_dataRootPath, "CharacterList.json");
        if (!File.Exists(filePath))
        {
            return Array.Empty<CharacterResourceItem>();
        }

        var json = File.ReadAllText(filePath, Encoding.UTF8);
        var entries = JsonSerializer.Deserialize<List<LegacyCharacterLocaleEntry>>(json, JsonOptions) ?? [];

        return entries
            .Select(entry => new CharacterResourceItem
            {
                Id = entry.Id,
                Role = entry.Role,
                ImageFileName = entry.ImageFileName,
                Abbrev = entry.Abbrev,
                FullSpell = entry.FullSpell,
                Names = entry.Names,
                Images = BuildCharacterImages(entry.Role, entry.ImageFileName)
            })
            .OrderBy(x => x.Role, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyCollection<MapResourceItem> LoadMaps()
    {
        var mapCatalogPath = Path.Combine(_dataRootPath, "MapList.json");
        if (!File.Exists(mapCatalogPath))
        {
            return Array.Empty<MapResourceItem>();
        }

        var json = File.ReadAllText(mapCatalogPath, Encoding.UTF8);
        var entries = JsonSerializer.Deserialize<List<MapCatalogEntry>>(json, JsonOptions) ?? [];

        return entries
            .Select(entry => new MapResourceItem
            {
                Id = entry.Id,
                AssetKey = entry.AssetKey,
                Names = entry.Names,
                Images = BuildMapImages(entry.AssetKey)
            })
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<ResourceImageMetadata> BuildCharacterImages(string role, string imageFileName)
    {
        var prefix = role == "survivor" ? "sur" : "hun";
        return new ResourceImageMetadata?[]
        {
            CreateImageMetadata($"{prefix}Big", imageFileName, "full"),
            CreateImageMetadata($"{prefix}Half", imageFileName, "half"),
            CreateImageMetadata($"{prefix}Header", imageFileName, "header"),
            CreateImageMetadata($"{prefix}Header_singleColor", imageFileName, "single-color")
        }
        .Where(x => x is not null)
        .Cast<ResourceImageMetadata>()
        .ToArray();
    }

    private IReadOnlyList<ResourceImageMetadata> BuildMapImages(string assetKey)
    {
        var images = new List<ResourceImageMetadata>();
        var primary = CreateImageMetadata("map", assetKey + ".png", "default");
        if (primary is not null)
        {
            images.Add(primary);
        }

        var square = CreateImageMetadata("map_square", assetKey + ".png", "square");
        if (square is not null)
        {
            images.Add(square);
        }

        var singleColor = CreateImageMetadata("map_singleColor", assetKey + ".png", "single-color");
        if (singleColor is not null)
        {
            images.Add(singleColor);
        }

        var rawDirectory = Path.Combine(_resourcesRootPath, "map_raw");
        if (Directory.Exists(rawDirectory))
        {
            foreach (var filePath in Directory.GetFiles(rawDirectory, assetKey + "*.*")
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(filePath);
                var metadata = CreateImageMetadata("map_raw", fileName, "raw", isPrimary: string.Equals(fileName, assetKey + ".webp", StringComparison.OrdinalIgnoreCase));
                if (metadata is not null)
                {
                    images.Add(metadata);
                }
            }
        }

        return images;
    }

    private ResourceImageMetadata? CreateImageMetadata(string folderName, string fileName, string variant, bool isPrimary = true)
    {
        var fullPath = Path.Combine(_resourcesRootPath, folderName, fileName);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var fileInfo = new FileInfo(fullPath);
        var relativePath = $"{folderName}/{fileName}";
        return new ResourceImageMetadata
        {
            Variant = variant,
            FileName = fileName,
            RelativePath = relativePath.Replace('\\', '/'),
            Url = BuildResourceUrl(folderName, fileName),
            ContentType = TryGetContentType(fileName),
            Extension = fileInfo.Extension,
            SizeBytes = fileInfo.Length,
            LastModifiedUtc = fileInfo.LastWriteTimeUtc,
            IsPrimary = isPrimary
        };
    }

    private static IReadOnlyCollection<ResourceImageMetadata> FilterImages(IReadOnlyList<ResourceImageMetadata>? images, IEnumerable<string>? variants)
    {
        if (images is null)
        {
            return Array.Empty<ResourceImageMetadata>();
        }

        var normalizedVariants = (variants ?? [])
            .Select(NormalizeVariant)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedVariants.Length == 0)
        {
            return images;
        }

        return images.Where(x => normalizedVariants.Contains(x.Variant, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private static string NormalizeVariant(string variant)
    {
        var normalized = variant.Trim().ToLowerInvariant();
        return normalized switch
        {
            "full-body" => "full",
            "fullbody" => "full",
            "half-body" => "half",
            "halfbody" => "half",
            "gray" => "single-color",
            "grey" => "single-color",
            "singlecolor" => "single-color",
            _ => normalized
        };
    }

    private string BuildResourceUrl(params string[] pathSegments)
        => "/resources/" + string.Join('/', pathSegments.Select(Uri.EscapeDataString));

    private string TryGetContentType(string fileName)
        => _contentTypeProvider.TryGetContentType(fileName, out var contentType) ? contentType : "application/octet-stream";
}
