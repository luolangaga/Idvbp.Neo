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
    Task<FrontendPackageInfo> ImportAsync(IFormFile file, CancellationToken cancellationToken = default);
    Task<FrontendPackageInfo> ImportAsync(string filePath, CancellationToken cancellationToken = default);
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

    public FrontendPackageService(string wwwrootPath)
    {
        _frontendsRoot = Path.Combine(wwwrootPath, "frontends");
        Directory.CreateDirectory(_frontendsRoot);
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

        var manifest = JsonSerializer.Deserialize<FrontendManifest>(
            File.ReadAllText(manifestPath),
            JsonOptions);
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

public sealed class FrontendManifest
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Type { get; set; }
    public string? EntryLayout { get; set; }
    public List<FrontendManifestPage> Pages { get; set; } = [];
}

public sealed class FrontendManifestPage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Layout { get; set; } = "";
}
