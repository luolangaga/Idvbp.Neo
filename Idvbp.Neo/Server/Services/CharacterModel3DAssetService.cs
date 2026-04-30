using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Idvbp.Neo.Server.Services;

public interface ICharacterModel3DAssetService
{
    Task<CharacterModel3DAssetImportResult> ImportAsync(
        IReadOnlyList<IFormFile> files,
        string category,
        string? primaryName,
        CancellationToken cancellationToken = default);
}

public sealed class CharacterModel3DAssetService : ICharacterModel3DAssetService
{
    private readonly string _assetRoot;

    public CharacterModel3DAssetService(string wwwrootPath)
    {
        _assetRoot = Path.Combine(wwwrootPath, "userdata", "character-model-3d");
        Directory.CreateDirectory(_assetRoot);
    }

    public async Task<CharacterModel3DAssetImportResult> ImportAsync(
        IReadOnlyList<IFormFile> files,
        string category,
        string? primaryName,
        CancellationToken cancellationToken = default)
    {
        var uploadFiles = files.Where(file => file.Length > 0 && !string.IsNullOrWhiteSpace(file.FileName)).ToArray();
        if (uploadFiles.Length == 0)
        {
            throw new ArgumentException("No files were uploaded.", nameof(files));
        }

        var safeCategory = SanitizeSegment(category);
        var batch = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var targetRoot = Path.Combine(_assetRoot, safeCategory, batch);
        Directory.CreateDirectory(targetRoot);

        var imported = new List<CharacterModel3DAssetFile>();
        foreach (var file in uploadFiles)
        {
            var safeName = SanitizeFileName(file.FileName);
            var targetPath = Path.GetFullPath(Path.Combine(targetRoot, safeName));
            if (!targetPath.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Invalid asset file name '{file.FileName}'.");
            }

            await using var input = file.OpenReadStream();
            await using var output = File.Create(targetPath);
            await input.CopyToAsync(output, cancellationToken);
            imported.Add(new CharacterModel3DAssetFile(
                safeName,
                BuildPublicUrl(safeCategory, batch, safeName),
                file.Length));
        }

        var primary = imported.FirstOrDefault(file => string.Equals(file.FileName, primaryName, StringComparison.OrdinalIgnoreCase))
                      ?? imported.First();
        return new CharacterModel3DAssetImportResult(primary.Url, imported);
    }

    private static string BuildPublicUrl(string category, string batch, string fileName)
        => "/userdata/character-model-3d/" + category + "/" + batch + "/" + Uri.EscapeDataString(fileName);

    private static string SanitizeSegment(string value)
    {
        var safe = string.Concat((value ?? "assets").Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "assets" : safe;
    }

    private static string SanitizeFileName(string value)
    {
        var fileName = Path.GetFileName(value.Replace('\\', '/'));
        var safe = string.Concat(fileName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(safe) ? Guid.NewGuid().ToString("N") : safe;
    }
}

public sealed record CharacterModel3DAssetImportResult(
    string PrimaryUrl,
    IReadOnlyList<CharacterModel3DAssetFile> Files);

public sealed record CharacterModel3DAssetFile(
    string FileName,
    string Url,
    long SizeBytes);
