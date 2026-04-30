using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Idvbp.Neo.Server.Services;

public interface IFrontendPackageService
{
    IReadOnlyCollection<FrontendPackageInfo> GetPackages();
    FrontendPackageInfo? GetPackage(string id);
    IReadOnlyCollection<FrontendComponentInfo> GetComponents();
    IReadOnlyCollection<FrontendFontInfo> GetFonts();
    Task<FrontendPackageInfo> ImportAsync(IFormFile file, CancellationToken cancellationToken = default);
    Task<FrontendPackageInfo> ImportAsync(string filePath, CancellationToken cancellationToken = default);
    Task<FrontendComponentInfo> ImportComponentAsync(string targetPackageId, ImportFrontendComponentRequest request, CancellationToken cancellationToken = default);
    Task<FrontendFontInfo> ImportFontAsync(IFormFile file, CancellationToken cancellationToken = default);
    Task WritePackageZipAsync(string id, Stream output, CancellationToken cancellationToken = default);
    Task SaveLayoutAsync(string id, string layoutPath, JsonElement layout, CancellationToken cancellationToken = default);
}

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

    public FrontendPackageService(string wwwrootPath)
    {
        _frontendsRoot = Path.Combine(wwwrootPath, "frontends");
        _fontsRoot = Path.Combine(wwwrootPath, "font");
        Directory.CreateDirectory(_frontendsRoot);
        Directory.CreateDirectory(_fontsRoot);
    }

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

    public FrontendPackageInfo? GetPackage(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var packagePath = Path.Combine(_frontendsRoot, SanitizePackageId(id));
        return Directory.Exists(packagePath) ? ReadPackage(packagePath) : null;
    }

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

    public async Task<FrontendPackageInfo> ImportAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
        {
            throw new ArgumentException("Package file is empty.", nameof(file));
        }

        await using var input = file.OpenReadStream();
        return await ImportCoreAsync(input, cancellationToken);
    }

    public async Task<FrontendPackageInfo> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Package file was not found.", filePath);
        }

        await using var input = File.OpenRead(filePath);
        return await ImportCoreAsync(input, cancellationToken);
    }

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

    private static FrontendComponentInfo ToComponentInfo(FrontendPackageInfo package, FrontendManifestComponent component)
        => new(
            package.Id,
            package.Name,
            component.Type,
            component.Script ?? string.Empty,
            component.Style ?? string.Empty);

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

    private static FrontendManifest ReadManifestFromFile(string manifestPath)
        => JsonSerializer.Deserialize<FrontendManifest>(File.ReadAllText(manifestPath), JsonOptions)
           ?? new FrontendManifest();

    private static async Task SaveManifestAsync(string manifestPath, FrontendManifest manifest, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions(JsonOptions)
        {
            WriteIndented = true
        }, cancellationToken);
    }

    private static IReadOnlyCollection<FrontendPageInfo> DiscoverPages(string packagePath, FrontendManifest manifest)
    {
        var manifestPages = manifest.Pages
            .Where(page => !string.IsNullOrWhiteSpace(page.Id) && !string.IsNullOrWhiteSpace(page.Layout))
            .Select(page => new FrontendPageInfo(
                page.Id,
                string.IsNullOrWhiteSpace(page.Name) ? page.Id : page.Name,
                NormalizeRelativePath(page.Layout)))
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
            manifestPages.Add(new FrontendPageInfo(id, id, relative));
            knownLayouts.Add(relative);
        }

        if (manifestPages.Count == 0)
        {
            manifestPages.Add(new FrontendPageInfo("default", "Default", manifest.EntryLayout ?? "layout.json"));
        }

        return manifestPages;
    }

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

    private static ZipArchiveEntry? FindManifestEntry(ZipArchive archive)
        => archive.Entries.FirstOrDefault(entry =>
            string.Equals(Path.GetFileName(entry.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase));

    private static async Task<FrontendManifest> ReadManifestAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<FrontendManifest>(stream, JsonOptions, cancellationToken);
        return manifest ?? throw new InvalidOperationException("manifest.json is invalid.");
    }

    private static string GetArchiveRootPrefix(string manifestPath)
    {
        var normalized = manifestPath.Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index < 0 ? string.Empty : normalized[..(index + 1)];
    }

    private static string StripPrefix(string value, string prefix)
    {
        var normalized = value.Replace('\\', '/');
        return string.IsNullOrEmpty(prefix) || !normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized[prefix.Length..];
    }

    private static string SanitizePackageId(string id)
        => string.Concat((id ?? string.Empty).Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'));

    private static string NormalizeRelativePath(string value)
        => value.Replace('\\', '/').TrimStart('/');
}

public sealed record FrontendPackageInfo(
    string Id,
    string Name,
    string Version,
    string Type,
    string EntryLayout,
    string LaunchUrl,
    string PhysicalPath,
    IReadOnlyCollection<FrontendPageInfo> Pages);

public sealed record FrontendPageInfo(string Id, string Name, string Layout);

public sealed record FrontendComponentInfo(
    string PackageId,
    string PackageName,
    string Type,
    string Script,
    string Style);

public sealed record FrontendFontInfo(string Family, string Url);

public sealed record ImportFrontendComponentRequest(string SourcePackageId, string Type);

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

public sealed class FrontendManifestPage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Layout { get; set; } = "";
}

public sealed class FrontendManifestComponent
{
    public string Type { get; set; } = "";
    public string? Script { get; set; }
    public string? Style { get; set; }
}
