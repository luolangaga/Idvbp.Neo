using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Idvbp.Neo.Server.Resources;

namespace Idvbp.Neo.Server.Services;

public interface IOfficialCharacterModelService
{
    Task<IReadOnlyDictionary<string, string>> GetModelMapAsync(CancellationToken cancellationToken = default);
    Task<OfficialModelResolveResult> ResolveAsync(string name, CancellationToken cancellationToken = default);
    Task<OfficialModelDownloadSummary> EnsureAllModelsAsync(IProgress<OfficialModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed partial class OfficialCharacterModelService : IOfficialCharacterModelService
{
    private const string RemoteListUrl = "https://id5.res.netease.com/pc/gw/20220408094220/js/app/data-gonglue_267f89f.js";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly HttpClient _httpClient = new();
    private readonly IResourceCatalogService _resourceCatalogService;
    private readonly string _modelsRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<OfficialModelEntry>? _entries;
    private IReadOnlyDictionary<string, OfficialModelEntry>? _lookup;

    public OfficialCharacterModelService(string wwwrootPath, IResourceCatalogService resourceCatalogService)
    {
        _resourceCatalogService = resourceCatalogService;
        _modelsRoot = Path.Combine(wwwrootPath, "official-models");
        Directory.CreateDirectory(_modelsRoot);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetModelMapAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _lookup!
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => BuildPublicModelUrl(group.First().Value), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<OfficialModelResolveResult> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        var key = NormalizeKey(name);
        if (string.IsNullOrWhiteSpace(key) || !_lookup!.TryGetValue(key, out var entry))
        {
            return new OfficialModelResolveResult(false, name, "", "", false, "model-not-found");
        }

        var downloaded = await EnsureDownloadedAsync(entry, cancellationToken);
        return new OfficialModelResolveResult(true, name, entry.DisplayName, BuildPublicModelUrl(entry), downloaded, "");
    }

    public async Task<OfficialModelDownloadSummary> EnsureAllModelsAsync(IProgress<OfficialModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        var entries = _entries!
            .GroupBy(x => x.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var downloaded = 0;
        var cached = 0;
        var failed = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];
            progress?.Report(new OfficialModelDownloadProgress(i + 1, entries.Length, entry.DisplayName, "downloading", ""));
            try
            {
                if (await EnsureDownloadedAsync(entry, cancellationToken))
                {
                    downloaded++;
                    progress?.Report(new OfficialModelDownloadProgress(i + 1, entries.Length, entry.DisplayName, "downloaded", ""));
                }
                else
                {
                    cached++;
                    progress?.Report(new OfficialModelDownloadProgress(i + 1, entries.Length, entry.DisplayName, "cached", ""));
                }
            }
            catch (Exception ex)
            {
                failed++;
                progress?.Report(new OfficialModelDownloadProgress(i + 1, entries.Length, entry.DisplayName, "failed", ex.Message));
            }
        }

        return new OfficialModelDownloadSummary(entries.Length, downloaded, cached, failed);
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_entries is not null && _lookup is not null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_entries is not null && _lookup is not null)
            {
                return;
            }

            var script = await _httpClient.GetStringAsync(RemoteListUrl, cancellationToken);
            var entries = ParseEntries(script).ToList();
            AttachLocalAliases(entries);
            _entries = entries;
            _lookup = entries
                .SelectMany(entry => entry.Aliases.Select(alias => new { Alias = NormalizeKey(alias), Entry = entry }))
                .Where(x => !string.IsNullOrWhiteSpace(x.Alias))
                .GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Entry, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _gate.Release();
        }
    }

    private IEnumerable<OfficialModelEntry> ParseEntries(string script)
    {
        foreach (var listName in new[] { "qszList", "jgzList" })
        {
            var arrayBody = ExtractArrayBody(script, listName);
            if (string.IsNullOrWhiteSpace(arrayBody))
            {
                continue;
            }

            foreach (var objectText in ExtractTopLevelObjects(arrayBody))
            {
                var model = ReadJsStringProperty(objectText, "model");
                if (string.IsNullOrWhiteSpace(model) || !Uri.TryCreate(model, UriKind.Absolute, out _))
                {
                    continue;
                }

                var name = ReadJsStringProperty(objectText, "name");
                var title = ReadJsStringProperty(objectText, "zy");
                var displayName = string.IsNullOrWhiteSpace(title) ? name : title;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = Path.GetFileNameWithoutExtension(new Uri(model).AbsolutePath);
                }

                var slug = SanitizePathSegment($"{(listName == "qszList" ? "survivor" : "hunter")}-{displayName}");
                yield return new OfficialModelEntry(
                    displayName,
                    model,
                    slug,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        displayName,
                        name,
                        title,
                        StripQuotes(displayName),
                        StripQuotes(title)
                    }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
            }
        }
    }

    private void AttachLocalAliases(IEnumerable<OfficialModelEntry> entries)
    {
        var entriesByName = entries
            .SelectMany(entry => entry.Aliases.Select(alias => new { Alias = NormalizeKey(alias), Entry = entry }))
            .Where(x => !string.IsNullOrWhiteSpace(x.Alias))
            .GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Entry, StringComparer.OrdinalIgnoreCase);

        foreach (var character in _resourceCatalogService.GetCharacters())
        {
            var candidateNames = character.Names.Values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Concat([character.Id, character.Abbrev ?? "", character.FullSpell ?? ""])
                .ToArray();

            var matched = candidateNames
                .Select(name => entriesByName.GetValueOrDefault(NormalizeKey(name)))
                .FirstOrDefault(entry => entry is not null);

            if (matched is null)
            {
                continue;
            }

            matched.Aliases.Add(character.Id);
            if (!string.IsNullOrWhiteSpace(character.Abbrev))
            {
                matched.Aliases.Add(character.Abbrev);
            }

            if (!string.IsNullOrWhiteSpace(character.FullSpell))
            {
                matched.Aliases.Add(character.FullSpell);
            }

            foreach (var name in character.Names.Values.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                matched.Aliases.Add(name!);
            }
        }
    }

    private async Task<bool> EnsureDownloadedAsync(OfficialModelEntry entry, CancellationToken cancellationToken)
    {
        var localModelPath = GetLocalModelPath(entry);
        if (File.Exists(localModelPath))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(localModelPath))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(localModelPath)!);
            await DownloadFileAsync(entry.ModelUrl, localModelPath, cancellationToken);
            if (string.Equals(Path.GetExtension(localModelPath), ".gltf", StringComparison.OrdinalIgnoreCase))
            {
                await DownloadGltfDependenciesAsync(entry.ModelUrl, localModelPath, cancellationToken);
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DownloadGltfDependenciesAsync(string remoteModelUrl, string localModelPath, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(localModelPath, Encoding.UTF8, cancellationToken), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var uris = new List<string>();
        if (document.RootElement.TryGetProperty("buffers", out var buffers) && buffers.ValueKind == JsonValueKind.Array)
        {
            uris.AddRange(buffers.EnumerateArray().Select(x => x.TryGetProperty("uri", out var uri) ? uri.GetString() : null).Where(x => !string.IsNullOrWhiteSpace(x))!);
        }

        if (document.RootElement.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            uris.AddRange(images.EnumerateArray().Select(x => x.TryGetProperty("uri", out var uri) ? uri.GetString() : null).Where(x => !string.IsNullOrWhiteSpace(x))!);
        }

        var remoteBase = new Uri(remoteModelUrl);
        var localBase = Path.GetDirectoryName(localModelPath)!;
        foreach (var uri in uris.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remoteUri = new Uri(remoteBase, uri);
            var localPath = Path.GetFullPath(Path.Combine(localBase, StripQuery(uri).Replace('/', Path.DirectorySeparatorChar)));
            if (!localPath.StartsWith(localBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!File.Exists(localPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await DownloadFileAsync(remoteUri.ToString(), localPath, cancellationToken);
            }
        }
    }

    private async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken);
    }

    private string BuildPublicModelUrl(OfficialModelEntry entry)
        => "/official-models/" + entry.Slug + "/" + Path.GetFileName(GetLocalModelPath(entry));

    private string GetLocalModelPath(OfficialModelEntry entry)
    {
        var fileName = Path.GetFileName(StripQuery(new Uri(entry.ModelUrl).AbsolutePath));
        return Path.Combine(_modelsRoot, entry.Slug, fileName);
    }

    private static string? ExtractArrayBody(string script, string variableName)
    {
        var marker = $"var {variableName}";
        var start = script.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        var bracketStart = script.IndexOf('[', start);
        if (bracketStart < 0) return null;

        var depth = 0;
        var quote = '\0';
        var escaped = false;
        for (var i = bracketStart; i < script.Length; i++)
        {
            var ch = script[i];
            if (quote != '\0')
            {
                escaped = !escaped && ch == '\\';
                if (!escaped && ch == quote) quote = '\0';
                if (ch != '\\') escaped = false;
                continue;
            }

            if (ch is '\'' or '"' or '`')
            {
                quote = ch;
                continue;
            }

            if (ch == '[') depth++;
            if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return script[(bracketStart + 1)..i];
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> ExtractTopLevelObjects(string arrayBody)
    {
        var depth = 0;
        var start = -1;
        var quote = '\0';
        var escaped = false;
        for (var i = 0; i < arrayBody.Length; i++)
        {
            var ch = arrayBody[i];
            if (quote != '\0')
            {
                escaped = !escaped && ch == '\\';
                if (!escaped && ch == quote) quote = '\0';
                if (ch != '\\') escaped = false;
                continue;
            }

            if (ch is '\'' or '"' or '`')
            {
                quote = ch;
                continue;
            }

            if (ch == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    yield return arrayBody[start..(i + 1)];
                    start = -1;
                }
            }
        }
    }

    private static string ReadJsStringProperty(string source, string property)
    {
        var match = Regex.Match(source, $@"(?s)(?:^|[,\{{\s]){Regex.Escape(property)}\s*:\s*(?<q>['""`])(?<v>.*?)(?<!\\)\k<q>");
        return match.Success ? Regex.Unescape(match.Groups["v"].Value).Trim() : "";
    }

    private static string NormalizeKey(string value)
        => StripQuotes(value).Trim().ToLowerInvariant();

    private static string StripQuotes(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("“", "", StringComparison.Ordinal)
                .Replace("”", "", StringComparison.Ordinal)
                .Replace("\"", "", StringComparison.Ordinal)
                .Replace("'", "", StringComparison.Ordinal);

    private static string SanitizePathSegment(string value)
    {
        var safe = string.Concat((value.Normalize(NormalizationForm.FormKC) ?? string.Empty)
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-'))
            .Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? Guid.NewGuid().ToString("N") : safe;
    }

    private static string StripQuery(string value)
    {
        var index = value.IndexOfAny(['?', '#']);
        return index >= 0 ? value[..index] : value;
    }

    private sealed record OfficialModelEntry(string DisplayName, string ModelUrl, string Slug, IReadOnlyCollection<string> InitialAliases)
    {
        public HashSet<string> Aliases { get; } = new(InitialAliases, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record OfficialModelResolveResult(
    bool Success,
    string RequestedName,
    string ModelName,
    string ModelUrl,
    bool Downloaded,
    string Error);

public sealed record OfficialModelDownloadProgress(
    int Current,
    int Total,
    string ModelName,
    string Status,
    string Error);

public sealed record OfficialModelDownloadSummary(
    int Total,
    int Downloaded,
    int Cached,
    int Failed);
