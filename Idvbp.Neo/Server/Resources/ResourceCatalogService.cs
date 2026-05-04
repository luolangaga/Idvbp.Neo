using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;

namespace Idvbp.Neo.Server.Resources;

/// <summary>
/// 资源目录服务接口，提供角色与地图资源的查询。
/// </summary>
public interface IResourceCatalogService
{
    /// <summary>
    /// 获取所有角色资源。
    /// </summary>
    IReadOnlyCollection<CharacterResourceItem> GetCharacters();

    /// <summary>
    /// 根据 ID 获取角色资源。
    /// </summary>
    CharacterResourceItem? GetCharacter(string id);

    /// <summary>
    /// 获取角色图片资源。
    /// </summary>
    IReadOnlyCollection<ResourceImageMetadata> GetCharacterImages(string id, IEnumerable<string>? variants);

    /// <summary>
    /// 获取所有地图资源。
    /// </summary>
    IReadOnlyCollection<MapResourceItem> GetMaps();

    /// <summary>
    /// 根据 ID 获取地图资源。
    /// </summary>
    MapResourceItem? GetMap(string id);

    /// <summary>
    /// 获取地图图片资源。
    /// </summary>
    IReadOnlyCollection<ResourceImageMetadata> GetMapImages(string id, IEnumerable<string>? variants);
}

/// <summary>
/// 资源目录服务实现，从本地 JSON 与文件系统加载角色和地图资源。
/// </summary>
public sealed class ResourceCatalogService : IResourceCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();
    private readonly string _resourcesRootPath;
    private readonly string _dataRootPath;
    private readonly IReadOnlyCollection<CharacterResourceItem> _characters;
    private readonly IReadOnlyCollection<MapResourceItem> _maps;

    /// <summary>
    /// 初始化资源目录服务。
    /// </summary>
    /// <param name="resourcesRootPath">资源根目录路径。</param>
    public ResourceCatalogService(string resourcesRootPath)
    {
        _resourcesRootPath = resourcesRootPath;
        _dataRootPath = Path.Combine(resourcesRootPath, "data");
        _contentTypeProvider.Mappings[".webp"] = "image/webp";
        _characters = LoadCharacters();
        _maps = LoadMaps();
    }

    /// <summary>
    /// 获取所有角色资源。
    /// </summary>
    public IReadOnlyCollection<CharacterResourceItem> GetCharacters() => _characters;

    /// <summary>
    /// 根据 ID 获取角色资源。
    /// </summary>
    public CharacterResourceItem? GetCharacter(string id)
        => _characters.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 获取角色图片资源。
    /// </summary>
    public IReadOnlyCollection<ResourceImageMetadata> GetCharacterImages(string id, IEnumerable<string>? variants)
        => FilterImages(GetCharacter(id)?.Images, variants);

    /// <summary>
    /// 获取所有地图资源。
    /// </summary>
    public IReadOnlyCollection<MapResourceItem> GetMaps() => _maps;

    /// <summary>
    /// 根据 ID 获取地图资源。
    /// </summary>
    public MapResourceItem? GetMap(string id)
        => _maps.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyCollection<ResourceImageMetadata> GetMapImages(string id, IEnumerable<string>? variants)
        => FilterImages(GetMap(id)?.Images, variants);

    /// <summary>
    /// 从 JSON 文件加载角色资源列表。
    /// </summary>
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

    /// <summary>
    /// 从 JSON 文件加载地图资源列表。
    /// </summary>
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

    /// <summary>
    /// 构建角色图片元数据列表。
    /// </summary>
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

    /// <summary>
    /// 构建地图图片元数据列表。
    /// </summary>
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

    /// <summary>
    /// 创建图片元数据，若文件不存在则返回 null。
    /// </summary>
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

    /// <summary>
    /// 根据变体过滤图片列表。
    /// </summary>
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

    /// <summary>
    /// 规范化图片变体名称。
    /// </summary>
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

    /// <summary>
    /// 构建资源 URL。
    /// </summary>
    private string BuildResourceUrl(params string[] pathSegments)
        => "/resources/" + string.Join('/', pathSegments.Select(Uri.EscapeDataString));

    /// <summary>
    /// 尝试获取文件的 MIME 类型。
    /// </summary>
    private string TryGetContentType(string fileName)
        => _contentTypeProvider.TryGetContentType(fileName, out var contentType) ? contentType : "application/octet-stream";
}
