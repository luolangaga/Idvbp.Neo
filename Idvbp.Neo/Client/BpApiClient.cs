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

public sealed class BpApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public BpApiClient(string serverUrls)
    {
        var baseAddress = ResolveBaseAddress(serverUrls);
        _httpClient = new HttpClient
        {
            BaseAddress = baseAddress
        };
    }

    public Uri BaseAddress => _httpClient.BaseAddress!;

    public async Task<IReadOnlyList<BpRoom>> GetRoomsAsync(CancellationToken cancellationToken = default)
        => await _httpClient.GetFromJsonAsync<List<BpRoom>>("api/rooms", cancellationToken) ?? [];

    public Task<BpRoom?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default)
        => _httpClient.GetFromJsonAsync<BpRoom>($"api/rooms/{Uri.EscapeDataString(roomId)}", cancellationToken);

    public async Task<IReadOnlyList<CharacterResourceItem>> GetCharactersAsync(CancellationToken cancellationToken = default)
        => await _httpClient.GetFromJsonAsync<List<CharacterResourceItem>>("api/resources/characters", cancellationToken) ?? [];

    public async Task<BpRoom> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/rooms", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    public async Task<BpRoom> CreateMatchAsync(string roomId, CreateMatchRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/matches", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    public async Task<BpRoom> SelectRoleAsync(string roomId, SelectRoleRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/roles", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

    public async Task<BpRoom> UpdateTeamsAsync(string roomId, UpdateRoomTeamsRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PatchAsJsonAsync($"api/rooms/{Uri.EscapeDataString(roomId)}/teams", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BpRoom>(cancellationToken))!;
    }

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

    public Task<Stream> GetStreamAsync(string url, CancellationToken cancellationToken = default)
        => _httpClient.GetStreamAsync(url, cancellationToken);

    public void Dispose()
    {
        _httpClient.Dispose();
    }

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
