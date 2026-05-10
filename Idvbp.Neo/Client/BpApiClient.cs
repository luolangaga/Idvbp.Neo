using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Idvbp.Neo.Models;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Server.Resources;

namespace Idvbp.Neo.Client;

/// <summary>
/// BP API 客户端，封装对后端 REST API 的调用。
/// </summary>
public sealed class BpApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 初始化 BP API 客户端。
    /// </summary>
    /// <param name="serverUrls">服务器地址列表。</param>
    public BpApiClient(string serverUrls)
    {
        var baseAddress = ResolveBaseAddress(serverUrls);
        _httpClient = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// 获取基础地址。
    /// </summary>
    public Uri BaseAddress => _httpClient.BaseAddress!;

    /// <summary>
    /// 获取房间列表（摘要，不含 logo 等大字段）。limit 为 null 时返回全部。
    /// </summary>
    public async Task<IReadOnlyList<RoomSummary>> GetRoomsAsync(int? limit = null, CancellationToken cancellationToken = default)
    {
        var url = limit.HasValue ? $"api/rooms?limit={limit.Value}" : "api/rooms";
        return await GetFromJsonCheckedAsync<List<RoomSummary>>(url, cancellationToken) ?? [];
    }

    /// <summary>
    /// 删除房间。
    /// </summary>
    public async Task<bool> DeleteRoomAsync(string roomId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync($"api/rooms/{Uri.EscapeDataString(roomId)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;
        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    /// <summary>
    /// 根据 ID 获取房间。
    /// </summary>
    public Task<BpRoom?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default)
        => GetFromJsonCheckedAsync<BpRoom>($"api/rooms/{Uri.EscapeDataString(roomId)}", cancellationToken);

    public Task<CurrentRoomPayload?> GetCurrentRoomAsync(CancellationToken cancellationToken = default)
        => GetFromJsonCheckedAsync<CurrentRoomPayload>("api/rooms/current", cancellationToken);

    public async Task<CurrentRoomPayload> SetCurrentRoomAsync(string? roomId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync("api/rooms/current", new SetCurrentRoomRequest { RoomId = roomId }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CurrentRoomPayload>(cancellationToken))!;
    }

    /// <summary>
    /// 获取所有角色资源。
    /// </summary>
    public async Task<IReadOnlyList<CharacterResourceItem>> GetCharactersAsync(CancellationToken cancellationToken = default)
        => await GetFromJsonCheckedAsync<List<CharacterResourceItem>>("api/resources/characters", cancellationToken) ?? [];

    /// <summary>
    /// 根据 ID 获取角色资源。
    /// </summary>
    public Task<CharacterResourceItem?> GetCharacterAsync(string characterId, CancellationToken cancellationToken = default)
        => GetFromJsonCheckedAsync<CharacterResourceItem>($"api/resources/characters/{Uri.EscapeDataString(characterId)}", cancellationToken);

    /// <summary>
    /// 获取角色图片资源。
    /// </summary>
    public async Task<IReadOnlyList<ResourceImageMetadata>> GetCharacterImagesAsync(string characterId, IEnumerable<string>? variants = null, CancellationToken cancellationToken = default)
    {
        var queryString = variants is not null && variants.Any()
            ? "?variant=" + string.Join("&variant=", variants.Select(Uri.EscapeDataString))
            : string.Empty;
        return await GetFromJsonCheckedAsync<List<ResourceImageMetadata>>($"api/resources/characters/{Uri.EscapeDataString(characterId)}/images{queryString}", cancellationToken) ?? [];
    }

    /// <summary>
    /// 获取所有地图资源。
    /// </summary>
    public async Task<IReadOnlyList<MapResourceItem>> GetMapsAsync(CancellationToken cancellationToken = default)
        => await GetFromJsonCheckedAsync<List<MapResourceItem>>("api/resources/maps", cancellationToken) ?? [];

    /// <summary>
    /// 根据 ID 获取地图资源。
    /// </summary>
    public Task<MapResourceItem?> GetMapAsync(string mapId, CancellationToken cancellationToken = default)
        => GetFromJsonCheckedAsync<MapResourceItem>($"api/resources/maps/{Uri.EscapeDataString(mapId)}", cancellationToken);

    /// <summary>
    /// 获取地图图片资源。
    /// </summary>
    public async Task<IReadOnlyList<ResourceImageMetadata>> GetMapImagesAsync(string mapId, IEnumerable<string>? variants = null, CancellationToken cancellationToken = default)
    {
        var queryString = variants is not null && variants.Any()
            ? "?variant=" + string.Join("&variant=", variants.Select(Uri.EscapeDataString))
            : string.Empty;
        return await GetFromJsonCheckedAsync<List<ResourceImageMetadata>>($"api/resources/maps/{Uri.EscapeDataString(mapId)}/images{queryString}", cancellationToken) ?? [];
    }

    /// <summary>
    /// 创建新房间。
    /// </summary>
    public async Task<BpRoom> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/rooms", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 为指定房间创建新对局。
    /// </summary>
    public async Task<BpRoom> CreateMatchAsync(string roomId, CreateMatchRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/matches", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 选择角色。
    /// </summary>
    public async Task<BpRoom> SelectRoleAsync(string roomId, SelectRoleRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/roles", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 更新房间队伍信息。
    /// </summary>
    public async Task<BpRoom> UpdateTeamsAsync(string roomId, UpdateRoomTeamsRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PatchAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/teams", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 添加禁用。
    /// </summary>
    public async Task<BpRoom> AddBanAsync(string roomId, AddBanRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/bans", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 添加全局禁用。
    /// </summary>
    public async Task<BpRoom> AddGlobalBanAsync(string roomId, AddBanRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/global-bans", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 更新地图。
    /// </summary>
    public async Task<BpRoom> UpdateMapAsync(string roomId, UpdateMapRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PatchAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/map", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 娣诲姞鍦板浘绂佺敤銆?
    /// </summary>
    public async Task<BpRoom> AddMapBanAsync(string roomId, AddMapBanRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/map-bans", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 更新房间阶段。
    /// </summary>
    public async Task<BpRoom> UpdatePhaseAsync(string roomId, UpdatePhaseRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PatchAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/phase", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 将相对 URL 转换为绝对 URL。
    /// </summary>
    public string? ToAbsoluteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        return new Uri(BaseAddress, url).ToString();
    }

    /// <summary>
    /// 获取资源流。
    /// </summary>
    public Task<Stream> GetStreamAsync(string url, CancellationToken cancellationToken = default)
        => _httpClient.GetStreamAsync(url, cancellationToken);

    /// <summary>
    /// 释放 HttpClient 资源。
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<T?> GetFromJsonCheckedAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryReadProblemMessage(content);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = string.IsNullOrWhiteSpace(response.ReasonPhrase)
                ? "HTTP 请求失败。"
                : response.ReasonPhrase;
        }

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string? TryReadProblemMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            return content.Length > 240 ? content[..240] + "..." : content;
        }

        return content.Length > 240 ? content[..240] + "..." : content;
    }

    /// <summary>
    /// 解析服务器地址列表，返回有效的基地址。
    /// </summary>
    private static Uri ResolveBaseAddress(string serverUrls)
    {
        var firstUrl = (serverUrls ?? string.Empty)
            .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (firstUrl is null || !Uri.TryCreate(firstUrl, UriKind.Absolute, out var baseAddress))
        {
            return new Uri("http://localhost:5000/");
        }

        return baseAddress.AbsolutePath == "/"
            ? baseAddress
            : new Uri(baseAddress.GetLeftPart(UriPartial.Authority) + "/");
    }
}
