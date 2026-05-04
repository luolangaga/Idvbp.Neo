using System.Collections.Generic;
using System.Linq;
using Idvbp.Neo.Server.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Idvbp.Neo.Server;

/// <summary>
/// 资源目录相关 API 端点定义。
/// </summary>
public static class ResourceApiEndpoints
{
    /// <summary>
    /// 映射资源目录相关的 REST API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    public static void MapResourceApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/resources/characters", (IResourceCatalogService catalogService) =>
            Results.Ok(catalogService.GetCharacters()));

        endpoints.MapGet("/api/resources/characters/{characterId}", (string characterId, IResourceCatalogService catalogService) =>
        {
            var character = catalogService.GetCharacter(characterId);
            return character is null
                ? Results.NotFound(new { message = $"Character '{characterId}' not found." })
                : Results.Ok(character);
        });

        endpoints.MapGet("/api/resources/characters/{characterId}/images", (string characterId, HttpContext context, IResourceCatalogService catalogService) =>
        {
            if (catalogService.GetCharacter(characterId) is null)
            {
                return Results.NotFound(new { message = $"Character '{characterId}' not found." });
            }

            var variants = ReadVariants(context.Request.Query["variant"]);
            return Results.Ok(catalogService.GetCharacterImages(characterId, variants));
        });

        endpoints.MapGet("/api/resources/maps", (IResourceCatalogService catalogService) =>
            Results.Ok(catalogService.GetMaps()));

        endpoints.MapGet("/api/resources/maps/{mapId}", (string mapId, IResourceCatalogService catalogService) =>
        {
            var map = catalogService.GetMap(mapId);
            return map is null
                ? Results.NotFound(new { message = $"Map '{mapId}' not found." })
                : Results.Ok(map);
        });

        endpoints.MapGet("/api/resources/maps/{mapId}/images", (string mapId, HttpContext context, IResourceCatalogService catalogService) =>
        {
            if (catalogService.GetMap(mapId) is null)
            {
                return Results.NotFound(new { message = $"Map '{mapId}' not found." });
            }

            var variants = ReadVariants(context.Request.Query["variant"]);
            return Results.Ok(catalogService.GetMapImages(mapId, variants));
        });
    }

    /// <summary>
    /// 从查询字符串中读取变体参数列表。
    /// </summary>
    private static IReadOnlyCollection<string> ReadVariants(Microsoft.Extensions.Primitives.StringValues values)
        => values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(x => x!.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
            .ToArray();
}
