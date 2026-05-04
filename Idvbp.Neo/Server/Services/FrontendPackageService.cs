using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Idvbp.Neo.Server.Services;

/// <summary>
/// 前端包服务接口，管理前端模板包、组件、字体与布局等资源。
/// </summary>
public interface IFrontendPackageService
{
    /// <summary>
    /// 获取所有前端包。
    /// </summary>
    IReadOnlyCollection<FrontendPackageInfo> GetPackages();

    /// <summary>
    /// 根据 ID 获取前端包。
    /// </summary>
    FrontendPackageInfo? GetPackage(string id);

    /// <summary>
    /// 获取所有前端组件。
    /// </summary>
    IReadOnlyCollection<FrontendComponentInfo> GetComponents();

    /// <summary>
    /// 获取设计器组件实例列表。
    /// </summary>
    IReadOnlyCollection<DesignerComponentInstanceInfo> GetDesignerComponentInstances(string packageId, string pageId);

    /// <summary>
    /// 获取所有字体。
    /// </summary>
    IReadOnlyCollection<FrontendFontInfo> GetFonts();

    /// <summary>
    /// 从上传文件导入前端包。
    /// </summary>
    Task<FrontendPackageInfo> ImportAsync(IFormFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从文件路径导入前端包。
    /// </summary>
    Task<FrontendPackageInfo> ImportAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 跨包导入组件。
    /// </summary>
    Task<FrontendComponentInfo> ImportComponentAsync(string targetPackageId, ImportFrontendComponentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建设计器组件。
    /// </summary>
    Task<DesignerComponentCreateResult> CreateDesignerComponentAsync(string targetPackageId, DesignerComponentCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 导入资源文件。
    /// </summary>
    Task<FrontendAssetImportResult> ImportAssetAsync(string targetPackageId, IFormFile file, string category, CancellationToken cancellationToken = default);

    /// <summary>
    /// 导入字体文件。
    /// </summary>
    Task<FrontendFontInfo> ImportFontAsync(IFormFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将前端包导出为 ZIP 流。
    /// </summary>
    Task WritePackageZipAsync(string id, Stream output, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存布局文件。
    /// </summary>
    Task SaveLayoutAsync(string id, string layoutPath, JsonElement layout, CancellationToken cancellationToken = default);
}

/// <summary>
/// 前端包服务实现，负责前端模板包、组件、字体与布局等资源的管理。
/// </summary>
public sealed class FrontendPackageService : IFrontendPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _frontendsRoot;
    private readonly string _fontsRoot;

    /// <summary>
    /// 初始化前端包服务。
    /// </summary>
    /// <param name="wwwrootPath">Web 根目录路径。</param>
    public FrontendPackageService(string wwwrootPath)
    {
        _frontendsRoot = Path.Combine(wwwrootPath, "frontends");
        _fontsRoot = Path.Combine(wwwrootPath, "font");
        Directory.CreateDirectory(_frontendsRoot);
        Directory.CreateDirectory(_fontsRoot);
    }

    /// <summary>
    /// 获取所有前端包。
    /// </summary>
    public IReadOnlyCollection<FrontendPackageInfo> GetPackages()
    {
        if (!Directory.Exists(_frontendsRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(_frontendsRoot)
            .Select(ReadPackage)
            .Where(package => package is not null)
            .Cast<FrontendPackageInfo>()
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 根据 ID 获取前端包。
    /// </summary>
    public FrontendPackageInfo? GetPackage(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var packagePath = Path.Combine(_frontendsRoot, SanitizePackageId(id));
        return Directory.Exists(packagePath) ? ReadPackage(packagePath) : null;
    }

    /// <summary>
    /// 获取所有前端组件。
    /// </summary>
    public IReadOnlyCollection<FrontendComponentInfo> GetComponents()
    {
        if (!Directory.Exists(_frontendsRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(_frontendsRoot)
            .SelectMany(ReadPackageComponents)
            .OrderBy(component => component.PackageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.Type, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 获取指定包页面的设计器组件实例列表。
    /// </summary>
    public IReadOnlyCollection<DesignerComponentInstanceInfo> GetDesignerComponentInstances(string packageId, string pageId)
    {
        var package = GetPackage(packageId) ?? throw new KeyNotFoundException($"Frontend package '{packageId}' was not found.");
        var page = package.Pages.FirstOrDefault(item =>
            string.Equals(item.Id, pageId, StringComparison.OrdinalIgnoreCase)) ??
            throw new KeyNotFoundException($"Frontend page '{pageId}' was not found.");

        var layoutPath = Path.GetFullPath(Path.Combine(
            package.PhysicalPath,
            page.Layout.Replace('/', Path.DirectorySeparatorChar)));
        var packagePath = Path.GetFullPath(package.PhysicalPath);
        if (!layoutPath.StartsWith(packagePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(layoutPath))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(layoutPath), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        if (!document.RootElement.TryGetProperty("nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<DesignerComponentInstanceInfo>();
        VisitDesignerNodes(nodes, result);
        return result;
    }

    /// <summary>
    /// 获取所有字体。
    /// </summary>
    public IReadOnlyCollection<FrontendFontInfo> GetFonts()
    {
        if (!Directory.Exists(_fontsRoot))
        {
            return [];
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ttf",
            ".otf",
            ".woff",
            ".woff2"
        };

        return Directory.EnumerateFiles(_fontsRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(file => allowed.Contains(Path.GetExtension(file)))
            .Select(file =>
            {
                var name = Path.GetFileNameWithoutExtension(file);
                return new FrontendFontInfo(name, $"/font/{Uri.EscapeDataString(Path.GetFileName(file))}");
            })
            .OrderBy(font => font.Family, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 从上传文件导入前端包。
    /// </summary>
    public async Task<FrontendPackageInfo> ImportAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
        {
            throw new ArgumentException("Package file is empty.", nameof(file));
        }

        await using var input = file.OpenReadStream();
        return await ImportCoreAsync(input, cancellationToken);
    }

    /// <summary>
    /// 从文件路径导入前端包。
    /// </summary>
    public async Task<FrontendPackageInfo> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Package file was not found.", filePath);
        }

        await using var input = File.OpenRead(filePath);
        return await ImportCoreAsync(input, cancellationToken);
    }

    /// <summary>
    /// 跨包导入组件。
    /// </summary>
    public async Task<FrontendComponentInfo> ImportComponentAsync(
        string targetPackageId,
        ImportFrontendComponentRequest request,
        CancellationToken cancellationToken = default)
    {
        var targetPackage = GetPackage(targetPackageId) ?? throw new KeyNotFoundException($"Frontend package '{targetPackageId}' was not found.");
        var sourcePackage = GetPackage(request.SourcePackageId) ?? throw new KeyNotFoundException($"Frontend package '{request.SourcePackageId}' was not found.");
        var targetManifestPath = Path.Combine(targetPackage.PhysicalPath, "manifest.json");
        var sourceManifestPath = Path.Combine(sourcePackage.PhysicalPath, "manifest.json");
        var targetManifest = ReadManifestFromFile(targetManifestPath);
        var sourceManifest = ReadManifestFromFile(sourceManifestPath);
        var sourceComponent = sourceManifest.Components.FirstOrDefault(component =>
            string.Equals(component.Type, request.Type, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Frontend component '{request.Type}' was not found.");

        var existing = targetManifest.Components.FirstOrDefault(component =>
            string.Equals(component.Type, sourceComponent.Type, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return ToComponentInfo(targetPackage, existing);
        }

        var importedComponent = new FrontendManifestComponent
        {
            Type = sourceComponent.Type,
            Script = await CopyComponentAssetAsync(sourcePackage.PhysicalPath, targetPackage.PhysicalPath, request.SourcePackageId, sourceComponent.Script, cancellationToken),
            Style = await CopyComponentAssetAsync(sourcePackage.PhysicalPath, targetPackage.PhysicalPath, request.SourcePackageId, sourceComponent.Style, cancellationToken)
        };

        targetManifest.Components.Add(importedComponent);
        await SaveManifestAsync(targetManifestPath, targetManifest, cancellationToken);
        return ToComponentInfo(targetPackage, importedComponent);
    }

    /// <summary>
    /// 创建设计器组件并可选添加到页面布局。
    /// </summary>
    public async Task<DesignerComponentCreateResult> CreateDesignerComponentAsync(
        string targetPackageId,
        DesignerComponentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var targetPackage = GetPackage(targetPackageId) ?? throw new KeyNotFoundException($"Frontend package '{targetPackageId}' was not found.");
        var componentType = SanitizePackageId(request.Type);
        if (string.IsNullOrWhiteSpace(componentType))
        {
            throw new ArgumentException("Component type is required.", nameof(request));
        }

        var targetManifestPath = Path.Combine(targetPackage.PhysicalPath, "manifest.json");
        var targetManifest = ReadManifestFromFile(targetManifestPath);
        var componentDirectory = Path.Combine(targetPackage.PhysicalPath, "components", "designer");
        Directory.CreateDirectory(componentDirectory);

        var scriptRelativePath = NormalizeRelativePath(Path.Combine("components", "designer", $"{componentType}.js"));
        var styleRelativePath = NormalizeRelativePath(Path.Combine("components", "designer", $"{componentType}.css"));
        var scriptPath = Path.Combine(targetPackage.PhysicalPath, scriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var stylePath = Path.Combine(targetPackage.PhysicalPath, styleRelativePath.Replace('/', Path.DirectorySeparatorChar));

        await File.WriteAllTextAsync(scriptPath, request.Script ?? string.Empty, cancellationToken);
        await File.WriteAllTextAsync(stylePath, request.Css ?? string.Empty, cancellationToken);

        var component = targetManifest.Components.FirstOrDefault(item =>
            string.Equals(item.Type, componentType, StringComparison.OrdinalIgnoreCase));
        if (component is null)
        {
            component = new FrontendManifestComponent
            {
                Type = componentType
            };
            targetManifest.Components.Add(component);
        }

        component.Script = scriptRelativePath;
        component.Style = string.IsNullOrWhiteSpace(request.Css) ? string.Empty : styleRelativePath;

        await SaveManifestAsync(targetManifestPath, targetManifest, cancellationToken);

        var nodeId = string.Empty;
        var layoutPath = string.Empty;
        if (request.AddToPage)
        {
            var addResult = await AddDesignerNodeToLayoutAsync(targetPackage, componentType, request, cancellationToken);
            nodeId = addResult.NodeId;
            layoutPath = addResult.LayoutPath;
        }

        return new DesignerComponentCreateResult(
            ToComponentInfo(targetPackage, component),
            nodeId,
            layoutPath,
            targetPackage.PhysicalPath,
            scriptPath,
            string.IsNullOrWhiteSpace(component.Style) ? string.Empty : stylePath);
    }

    /// <summary>
    /// 导入资源文件到指定包。
    /// </summary>
    public async Task<FrontendAssetImportResult> ImportAssetAsync(
        string targetPackageId,
        IFormFile file,
        string category,
        CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
        {
            throw new ArgumentException("Asset file is empty.", nameof(file));
        }

        var targetPackage = GetPackage(targetPackageId) ?? throw new KeyNotFoundException($"Frontend package '{targetPackageId}' was not found.");
        var safeCategory = SanitizePackageId(category);
        if (string.IsNullOrWhiteSpace(safeCategory))
        {
            safeCategory = "assets";
        }

        var extension = Path.GetExtension(file.FileName);
        var baseName = SanitizePackageId(Path.GetFileNameWithoutExtension(file.FileName));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"asset-{Guid.NewGuid():N}";
        }

        var packageFullPath = Path.GetFullPath(targetPackage.PhysicalPath);
        var targetDirectory = Path.GetFullPath(Path.Combine(packageFullPath, "assets", safeCategory));
        if (!targetDirectory.StartsWith(packageFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Asset import path escapes package root.");
        }

        Directory.CreateDirectory(targetDirectory);
        var targetName = $"{baseName}{extension.ToLowerInvariant()}";
        var targetPath = Path.Combine(targetDirectory, targetName);
        var index = 1;
        while (File.Exists(targetPath))
        {
            targetName = $"{baseName}-{index++}{extension.ToLowerInvariant()}";
            targetPath = Path.Combine(targetDirectory, targetName);
        }

        await using var source = file.OpenReadStream();
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target, cancellationToken);

        var relativePath = NormalizeRelativePath(Path.GetRelativePath(packageFullPath, targetPath));
        return new FrontendAssetImportResult(
            targetName,
            relativePath,
            $"/frontends/{Uri.EscapeDataString(targetPackage.Id)}/{relativePath}",
            file.Length);
    }

    /// <summary>
    /// 导入字体文件。
    /// </summary>
    public async Task<FrontendFontInfo> ImportFontAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
        {
            throw new ArgumentException("Font file is empty.", nameof(file));
        }

        var extension = Path.GetExtension(file.FileName);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ttf",
            ".otf",
            ".woff",
            ".woff2"
        };
        if (!allowed.Contains(extension))
        {
            throw new InvalidOperationException("Only ttf, otf, woff, and woff2 font files are supported.");
        }

        Directory.CreateDirectory(_fontsRoot);
        var baseName = SanitizePackageId(Path.GetFileNameWithoutExtension(file.FileName));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"font-{Guid.NewGuid():N}";
        }

        var targetName = $"{baseName}{extension.ToLowerInvariant()}";
        var targetPath = Path.Combine(_fontsRoot, targetName);
        var index = 1;
        while (File.Exists(targetPath))
        {
            targetName = $"{baseName}-{index++}{extension.ToLowerInvariant()}";
            targetPath = Path.Combine(_fontsRoot, targetName);
        }

        await using var source = file.OpenReadStream();
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target, cancellationToken);

        return new FrontendFontInfo(Path.GetFileNameWithoutExtension(targetName), $"/font/{Uri.EscapeDataString(targetName)}");
    }

    /// <summary>
    /// 核心导入逻辑，将 ZIP 包解压到前端包目录。
    /// </summary>
    private async Task<FrontendPackageInfo> ImportCoreAsync(Stream input, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = FindManifestEntry(archive) ?? throw new InvalidOperationException("Package must contain manifest.json.");
        var manifest = await ReadManifestAsync(manifestEntry, cancellationToken);
        var packageId = SanitizePackageId(manifest.Id);
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException("manifest.json id is required.");
        }

        var destination = Path.Combine(_frontendsRoot, packageId);
        var destinationFullPath = Path.GetFullPath(destination);
        var tempPath = Path.Combine(_frontendsRoot, $".import-{Guid.NewGuid():N}");
        var tempFullPath = Path.GetFullPath(tempPath);

        Directory.CreateDirectory(tempFullPath);
        try
        {
            var prefix = GetArchiveRootPrefix(manifestEntry.FullName);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var relativePath = StripPrefix(entry.FullName, prefix);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(tempFullPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(tempFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Package entry '{entry.FullName}' escapes package root.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var source = entry.Open();
                await using var targetStream = File.Create(target);
                await source.CopyToAsync(targetStream, cancellationToken);
            }

            if (Directory.Exists(destinationFullPath))
            {
                Directory.Delete(destinationFullPath, recursive: true);
            }

            Directory.Move(tempFullPath, destinationFullPath);
        }
        catch
        {
            if (Directory.Exists(tempFullPath))
            {
                Directory.Delete(tempFullPath, recursive: true);
            }

            throw;
        }

        return GetPackage(packageId) ?? throw new InvalidOperationException("Imported package could not be read.");
    }

    /// <summary>
    /// 将前端包导出为 ZIP 流。
    /// </summary>
    public async Task WritePackageZipAsync(string id, Stream output, CancellationToken cancellationToken = default)
    {
        var package = GetPackage(id) ?? throw new KeyNotFoundException($"Frontend package '{id}' was not found.");
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in Directory.EnumerateFiles(package.PhysicalPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(package.PhysicalPath, file).Replace('\\', '/');
            var entry = archive.CreateEntry($"{package.Id}/{relativePath}", CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var fileStream = File.OpenRead(file);
            await fileStream.CopyToAsync(entryStream, cancellationToken);
        }
    }

    /// <summary>
    /// 保存布局文件。
    /// </summary>
    public async Task SaveLayoutAsync(string id, string layoutPath, JsonElement layout, CancellationToken cancellationToken = default)
    {
        var package = GetPackage(id) ?? throw new KeyNotFoundException($"Frontend package '{id}' was not found.");
        var normalizedLayoutPath = NormalizeRelativePath(layoutPath);
        if (string.IsNullOrWhiteSpace(normalizedLayoutPath))
        {
            throw new ArgumentException("Layout path is required.", nameof(layoutPath));
        }

        var packageFullPath = Path.GetFullPath(package.PhysicalPath);
        var targetPath = Path.GetFullPath(Path.Combine(packageFullPath, normalizedLayoutPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!targetPath.StartsWith(packageFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Layout path escapes package root.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await using var stream = File.Create(targetPath);
        await JsonSerializer.SerializeAsync(stream, layout, new JsonSerializerOptions(JsonOptions)
        {
            WriteIndented = true
        }, cancellationToken);
    }

    /// <summary>
    /// 将设计器组件节点添加到布局文件中。
    /// </summary>
    private static async Task<DesignerLayoutNodeCreateResult> AddDesignerNodeToLayoutAsync(
        FrontendPackageInfo package,
        string componentType,
        DesignerComponentCreateRequest request,
        CancellationToken cancellationToken)
    {
        var layoutPath = NormalizeRelativePath(request.LayoutPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(layoutPath))
        {
            var page = package.Pages.FirstOrDefault(item =>
                string.Equals(item.Id, request.PageId, StringComparison.OrdinalIgnoreCase)) ??
                package.Pages.FirstOrDefault();
            layoutPath = page?.Layout ?? package.EntryLayout;
        }

        if (string.IsNullOrWhiteSpace(layoutPath))
        {
            throw new InvalidOperationException("Target layout path could not be resolved.");
        }

        var packageFullPath = Path.GetFullPath(package.PhysicalPath);
        var targetPath = Path.GetFullPath(Path.Combine(packageFullPath, layoutPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!targetPath.StartsWith(packageFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Layout path escapes package root.");
        }

        var layout = File.Exists(targetPath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(targetPath, cancellationToken)) as JsonObject
            : null;
        layout ??= new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = $"{package.Id}-layout",
            ["canvas"] = new JsonObject
            {
                ["width"] = 1920,
                ["height"] = 1080,
                ["scaleMode"] = "contain"
            }
        };

        var nodes = layout["nodes"] as JsonArray;
        if (nodes is null)
        {
            nodes = [];
            layout["nodes"] = nodes;
        }

        var preferredNodeId = SanitizePackageId(request.NodeId ?? string.Empty);
        var existingNode = string.IsNullOrWhiteSpace(preferredNodeId) ? null : FindLayoutNode(nodes, preferredNodeId);
        var nodeId = existingNode?["id"]?.GetValue<string>() ?? CreateUniqueNodeId(nodes, string.IsNullOrWhiteSpace(preferredNodeId) ? componentType : preferredNodeId);
        var configBind = $"configs.{nodeId}";
        var node = existingNode ?? new JsonObject();
        node["id"] = nodeId;
        node["type"] = componentType;
        node["props"] = new JsonObject
        {
            ["room"] = new JsonObject { ["bind"] = "room" },
            ["event"] = new JsonObject { ["bind"] = "event" },
            ["config"] = new JsonObject { ["bind"] = configBind }
        };
        node["style"] = new JsonObject
        {
            ["left"] = Math.Max(0, request.Left ?? 80),
            ["top"] = Math.Max(0, request.Top ?? 80),
            ["width"] = Math.Clamp(request.Width ?? 360, 20, 7680),
            ["height"] = Math.Clamp(request.Height ?? 220, 20, 4320),
            ["zIndex"] = request.ZIndex ?? 20
        };

        var designerRoomEvents = new[]
        {
            "room.snapshot", "room.info.updated", "match.created",
            "room.map.updated", "room.ban.updated", "room.global-ban.updated",
            "room.role.selected", "room.phase.updated"
        };
        var eventsNode = existingNode?["events"] as JsonObject ?? [];
        foreach (var evt in designerRoomEvents)
        {
            if (!eventsNode.ContainsKey(evt))
            {
                eventsNode[evt] = new JsonArray(new JsonObject { ["action"] = "syncState" });
            }
        }
        node["events"] = eventsNode;

        if (existingNode is null)
        {
            nodes.Add(node);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await File.WriteAllTextAsync(
            targetPath,
            layout.ToJsonString(new JsonSerializerOptions(JsonOptions) { WriteIndented = true }),
            cancellationToken);

        return new DesignerLayoutNodeCreateResult(nodeId, layoutPath, targetPath);
    }

    /// <summary>
    /// 在布局节点中创建唯一节点 ID。
    /// </summary>
    private static string CreateUniqueNodeId(JsonArray nodes, string type)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Visit(nodes);

        var baseId = SanitizePackageId(type);
        var candidate = baseId;
        var index = 1;
        while (existing.Contains(candidate))
        {
            candidate = $"{baseId}-{index++}";
        }

        return candidate;

        void Visit(JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not JsonObject node)
                {
                    continue;
                }

                var id = node["id"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    existing.Add(id);
                }

                if (node["children"] is JsonArray children)
                {
                    Visit(children);
                }
            }
        }
    }

    /// <summary>
    /// 在布局节点树中查找指定 ID 的节点。
    /// </summary>
    private static JsonObject? FindLayoutNode(JsonArray nodes, string nodeId)
    {
        foreach (var item in nodes)
        {
            if (item is not JsonObject node)
            {
                continue;
            }

            var id = node["id"]?.GetValue<string>();
            if (string.Equals(id, nodeId, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            if (node["children"] is JsonArray children)
            {
                var child = FindLayoutNode(children, nodeId);
                if (child is not null)
                {
                    return child;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 读取指定路径的前端包信息。
    /// </summary>
    private FrontendPackageInfo? ReadPackage(string packagePath)
    {
        var manifestPath = Path.Combine(packagePath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var manifest = ReadManifestFromFile(manifestPath);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
        {
            return null;
        }

        var pages = DiscoverPages(packagePath, manifest);
        return new FrontendPackageInfo(
            manifest.Id,
            string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name,
            manifest.Version ?? string.Empty,
            manifest.Type ?? "layout-template",
            manifest.EntryLayout ?? "layout.json",
            $"/bp-layout?frontend={Uri.EscapeDataString(manifest.Id)}",
            packagePath,
            pages);
    }

    /// <summary>
    /// 读取指定包路径的所有组件信息。
    /// </summary>
    private IReadOnlyCollection<FrontendComponentInfo> ReadPackageComponents(string packagePath)
    {
        var package = ReadPackage(packagePath);
        if (package is null)
        {
            return [];
        }

        var manifest = ReadManifestFromFile(Path.Combine(packagePath, "manifest.json"));
        return manifest.Components
            .Where(component => !string.IsNullOrWhiteSpace(component.Type))
            .Select(component => ToComponentInfo(package, component))
            .ToArray();
    }

    /// <summary>
    /// 将清单组件转换为组件信息。
    /// </summary>
    private static FrontendComponentInfo ToComponentInfo(FrontendPackageInfo package, FrontendManifestComponent component)
        => new(
            package.Id,
            package.Name,
            component.Type,
            component.Script ?? string.Empty,
            component.Style ?? string.Empty);

    /// <summary>
    /// 复制组件资源文件到目标包。
    /// </summary>
    private async Task<string> CopyComponentAssetAsync(
        string sourcePackagePath,
        string targetPackagePath,
        string sourcePackageId,
        string? relativePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var normalized = NormalizeRelativePath(relativePath);
        var sourceFullPath = Path.GetFullPath(Path.Combine(sourcePackagePath, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var sourceRoot = Path.GetFullPath(sourcePackagePath);
        if (!sourceFullPath.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourceFullPath))
        {
            throw new InvalidOperationException($"Component asset '{relativePath}' is invalid.");
        }

        var targetRelativePath = NormalizeRelativePath(Path.Combine(
            "components",
            "imported",
            SanitizePackageId(sourcePackageId),
            normalized).Replace('\\', '/'));
        var targetFullPath = Path.GetFullPath(Path.Combine(targetPackagePath, targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var targetRoot = Path.GetFullPath(targetPackagePath);
        if (!targetFullPath.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Component import path escapes package root.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetFullPath)!);
        await using var source = File.OpenRead(sourceFullPath);
        await using var target = File.Create(targetFullPath);
        await source.CopyToAsync(target, cancellationToken);
        return targetRelativePath;
    }

    /// <summary>
    /// 从文件读取清单。
    /// </summary>
    private static FrontendManifest ReadManifestFromFile(string manifestPath)
        => JsonSerializer.Deserialize<FrontendManifest>(File.ReadAllText(manifestPath), JsonOptions)
           ?? new FrontendManifest();

    /// <summary>
    /// 异步保存清单到文件。
    /// </summary>
    private static async Task SaveManifestAsync(string manifestPath, FrontendManifest manifest, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions(JsonOptions)
        {
            WriteIndented = true
        }, cancellationToken);
    }

    /// <summary>
    /// 发现包中的所有页面。
    /// </summary>
    private static IReadOnlyCollection<FrontendPageInfo> DiscoverPages(string packagePath, FrontendManifest manifest)
    {
        var manifestPages = manifest.Pages
            .Where(page => !string.IsNullOrWhiteSpace(page.Id) && !string.IsNullOrWhiteSpace(page.Layout))
            .Select(page =>
            {
                var layout = NormalizeRelativePath(page.Layout);
                var canvas = ReadLayoutCanvas(Path.Combine(packagePath, layout.Replace('/', Path.DirectorySeparatorChar)));
                return new FrontendPageInfo(
                    page.Id,
                    string.IsNullOrWhiteSpace(page.Name) ? page.Id : page.Name,
                    layout,
                    canvas.Width,
                    canvas.Height);
            })
            .ToList();

        var knownLayouts = new HashSet<string>(manifestPages.Select(page => page.Layout), StringComparer.OrdinalIgnoreCase);
        foreach (var layoutFile in Directory.EnumerateFiles(packagePath, "*.json", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(layoutFile), "manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = NormalizeRelativePath(Path.GetRelativePath(packagePath, layoutFile));
            if (knownLayouts.Contains(relative) || !LooksLikeLayout(layoutFile))
            {
                continue;
            }

            var id = Path.GetFileNameWithoutExtension(layoutFile);
            var canvas = ReadLayoutCanvas(layoutFile);
            manifestPages.Add(new FrontendPageInfo(id, id, relative, canvas.Width, canvas.Height));
            knownLayouts.Add(relative);
        }

        if (manifestPages.Count == 0)
        {
            var layout = NormalizeRelativePath(manifest.EntryLayout ?? "layout.json");
            var canvas = ReadLayoutCanvas(Path.Combine(packagePath, layout.Replace('/', Path.DirectorySeparatorChar)));
            manifestPages.Add(new FrontendPageInfo("default", "Default", layout, canvas.Width, canvas.Height));
        }

        return manifestPages;
    }

    /// <summary>
    /// 读取布局画布的尺寸信息。
    /// </summary>
    private static FrontendPageCanvas ReadLayoutCanvas(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return FrontendPageCanvas.Default;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("canvas", out var canvas) ||
                canvas.ValueKind != JsonValueKind.Object)
            {
                return FrontendPageCanvas.Default;
            }

            var width = canvas.TryGetProperty("width", out var widthElement) && widthElement.TryGetInt32(out var parsedWidth)
                ? parsedWidth
                : FrontendPageCanvas.Default.Width;
            var height = canvas.TryGetProperty("height", out var heightElement) && heightElement.TryGetInt32(out var parsedHeight)
                ? parsedHeight
                : FrontendPageCanvas.Default.Height;
            return new FrontendPageCanvas(
                Math.Clamp(width, 320, 7680),
                Math.Clamp(height, 240, 4320));
        }
        catch
        {
            return FrontendPageCanvas.Default;
        }
    }

    /// <summary>
    /// 判断文件是否为布局文件。
    /// </summary>
    private static bool LooksLikeLayout(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("nodes", out var nodes) &&
                   nodes.ValueKind == JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 在 ZIP 归档中查找清单文件。
    /// </summary>
    private static ZipArchiveEntry? FindManifestEntry(ZipArchive archive)
        => archive.Entries.FirstOrDefault(entry =>
            string.Equals(Path.GetFileName(entry.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 从 ZIP 条目异步读取清单。
    /// </summary>
    private static async Task<FrontendManifest> ReadManifestAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<FrontendManifest>(stream, JsonOptions, cancellationToken);
        return manifest ?? throw new InvalidOperationException("manifest.json is invalid.");
    }

    /// <summary>
    /// 获取 ZIP 归档根目录前缀。
    /// </summary>
    private static string GetArchiveRootPrefix(string manifestPath)
    {
        var normalized = manifestPath.Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index < 0 ? string.Empty : normalized[..(index + 1)];
    }

    /// <summary>
    /// 去除路径前缀。
    /// </summary>
    private static string StripPrefix(string value, string prefix)
    {
        var normalized = value.Replace('\\', '/');
        return string.IsNullOrEmpty(prefix) || !normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized[prefix.Length..];
    }

    /// <summary>
    /// 清理包 ID，仅保留字母、数字、连字符、下划线和点。
    /// </summary>
    private static string SanitizePackageId(string id)
        => string.Concat((id ?? string.Empty).Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'));

    /// <summary>
    /// 规范化相对路径，统一为正斜杠并去除前导斜杠。
    /// </summary>
    private static string NormalizeRelativePath(string value)
        => value.Replace('\\', '/').TrimStart('/');

    /// <summary>
    /// 遍历设计器节点，收集组件实例信息。
    /// </summary>
    private static void VisitDesignerNodes(JsonElement nodes, List<DesignerComponentInstanceInfo> result)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = node.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
            var type = node.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(id) &&
                !string.IsNullOrWhiteSpace(type) &&
                FileNameSafeEquals(type, SanitizePackageId(type)))
            {
                result.Add(new DesignerComponentInstanceInfo(id, type));
            }

            if (node.TryGetProperty("children", out var children) &&
                children.ValueKind == JsonValueKind.Array)
            {
                VisitDesignerNodes(children, result);
            }
        }
    }

    /// <summary>
    /// 判断值是否为文件名安全的组件类型。
    /// </summary>
    private static bool FileNameSafeEquals(string value, string sanitized)
        => string.Equals(value, sanitized, StringComparison.Ordinal) &&
           !value.StartsWith("asg-bp-", StringComparison.OrdinalIgnoreCase) &&
           !value.StartsWith("bp-", StringComparison.OrdinalIgnoreCase) &&
           !value.StartsWith("team-card", StringComparison.OrdinalIgnoreCase) &&
           !value.StartsWith("character-model", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 前端包信息记录。
/// </summary>
public sealed record FrontendPackageInfo(
    string Id,
    string Name,
    string Version,
    string Type,
    string EntryLayout,
    string LaunchUrl,
    string PhysicalPath,
    IReadOnlyCollection<FrontendPageInfo> Pages);

/// <summary>
/// 前端页面信息记录。
/// </summary>
public sealed record FrontendPageInfo(string Id, string Name, string Layout, int CanvasWidth, int CanvasHeight);

/// <summary>
/// 前端页面画布尺寸记录。
/// </summary>
public sealed record FrontendPageCanvas(int Width, int Height)
{
    /// <summary>
    /// 默认画布尺寸（1280x720）。
    /// </summary>
    public static FrontendPageCanvas Default { get; } = new(1280, 720);
}

/// <summary>
/// 前端组件信息记录。
/// </summary>
public sealed record FrontendComponentInfo(
    string PackageId,
    string PackageName,
    string Type,
    string Script,
    string Style);

/// <summary>
/// 前端字体信息记录。
/// </summary>
public sealed record FrontendFontInfo(string Family, string Url);

/// <summary>
/// 设计器组件实例信息记录。
/// </summary>
public sealed record DesignerComponentInstanceInfo(string NodeId, string Type);

/// <summary>
/// 前端资源导入结果记录。
/// </summary>
public sealed record FrontendAssetImportResult(
    string FileName,
    string RelativePath,
    string Url,
    long SizeBytes);

/// <summary>
/// 跨包导入组件请求记录。
/// </summary>
public sealed record ImportFrontendComponentRequest(string SourcePackageId, string Type);

/// <summary>
/// 设计器组件创建结果记录。
/// </summary>
public sealed record DesignerComponentCreateResult(
    FrontendComponentInfo Component,
    string NodeId,
    string LayoutPath,
    string PackagePath,
    string ScriptPath,
    string StylePath);

/// <summary>
/// 设计器布局节点创建结果记录（内部）。
/// </summary>
internal sealed record DesignerLayoutNodeCreateResult(string NodeId, string LayoutPath, string PhysicalPath);

/// <summary>
/// 设计器组件创建请求记录。
/// </summary>
public sealed record DesignerComponentCreateRequest(
    string Type,
    string? Script,
    string? Css,
    bool AddToPage,
    string? PageId,
    string? LayoutPath,
    string? NodeId,
    int? Left,
    int? Top,
    int? Width,
    int? Height,
    int? ZIndex);

/// <summary>
/// 前端清单模型。
/// </summary>
public sealed class FrontendManifest
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Type { get; set; }
    public string? EntryLayout { get; set; }
    public List<FrontendManifestPage> Pages { get; set; } = [];
    public List<FrontendManifestComponent> Components { get; set; } = [];
}

/// <summary>
/// 前端清单页面模型。
/// </summary>
public sealed class FrontendManifestPage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Layout { get; set; } = "";
}

/// <summary>
/// 前端清单组件模型。
/// </summary>
public sealed class FrontendManifestComponent
{
    public string Type { get; set; } = "";
    public string? Script { get; set; }
    public string? Style { get; set; }
}
