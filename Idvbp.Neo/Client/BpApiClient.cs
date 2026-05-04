using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
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
            BaseAddress = baseAddress
        };
    }

    /// <summary>
    /// 获取基础地址。
    /// </summary>
    public Uri BaseAddress => _httpClient.BaseAddress!;

    /// <summary>
    /// 获取所有房间列表。
    /// </summary>
    public async Task<IReadOnlyList<BpRoom>> GetRoomsAsync(CancellationToken cancellationToken = default)
        => await _httpClient.GetFromJsonAsync<List<BpRoom>>("api/rooms", cancellationToken) ?? [];

    /// <summary>
    /// 根据 ID 获取房间。
    /// </summary>
    public Task<BpRoom?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default)
        => _httpClient.GetFromJsonAsync<BpRoom>($"api/rooms/{Uri.EscapeDataString(roomId)}", cancellationToken);

    /// <summary>
    /// 获取所有角色资源。
    /// </summary>
    public async Task<IReadOnlyList<CharacterResourceItem>> GetCharactersAsync(CancellationToken cancellationToken = default)
        => await _httpClient.GetFromJsonAsync<List<CharacterResourceItem>>("api/resources/characters", cancellationToken) ?? [];

    /// <summary>
    /// 创建新房间。
    /// </summary>
    public async Task<BpRoom> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/rooms", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 为指定房间创建新对局。
    /// </summary>
    public async Task<BpRoom> CreateMatchAsync(string roomId, CreateMatchRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/matches", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 选择角色。
    /// </summary>
    public async Task<BpRoom> SelectRoleAsync(string roomId, SelectRoleRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/roles", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    /// <summary>
    /// 更新房间队伍信息。
    /// </summary>
    public async Task<BpRoom> UpdateTeamsAsync(string roomId, UpdateRoomTeamsRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PatchAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/teams", request, cancellationToken);
        response.EnsureSuccessStatusCode();
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
