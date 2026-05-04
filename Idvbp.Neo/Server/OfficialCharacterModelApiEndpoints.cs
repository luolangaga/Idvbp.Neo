using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Idvbp.Neo.Models;
using Idvbp.Neo.Models.Enums;
using Idvbp.Neo.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Idvbp.Neo.Server;

/// <summary>
/// 官方角色模型相关 API 端点定义。
/// </summary>
public static class OfficialCharacterModelApiEndpoints
{
    /// <summary>
    /// 映射官方角色模型相关的 REST API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    public static void MapOfficialCharacterModelApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/official-model-map", async (IOfficialCharacterModelService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetModelMapAsync(cancellationToken)));

        endpoints.MapGet("/api/official-models/resolve", async (string name, IOfficialCharacterModelService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ResolveAsync(name, cancellationToken);
            return result.Success
                ? Results.Ok(result)
                : Results.NotFound(result);
        });

        endpoints.MapPost("/api/official-models/ensure-all", async (IOfficialCharacterModelService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.EnsureAllModelsAsync(null, cancellationToken)));

        endpoints.MapGet("/api/local-bp-state", async (IRoomService roomService, IProxyPageConfigRepository pageConfigRepository, CancellationToken cancellationToken) =>
        {
            var room = (await roomService.GetRoomsAsync(cancellationToken))
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefault();
            var layoutJson = pageConfigRepository.GetValueOrDefault("frontend:character-model-3d:main:component:character-model-stage");
            if (string.IsNullOrWhiteSpace(layoutJson))
            {
                layoutJson = pageConfigRepository.GetValueOrDefault("frontend:character-model-3d:main");
            }

            if (room is null)
            {
                return Results.Ok(new
                {
                    success = true,
                    data = new CharacterModelBpState
                    {
                        CharacterModel3DLayout = CharacterModelBpState.TryParseLayout(layoutJson)
                    }
                });
            }

            return Results.Ok(new
            {
                success = true,
                data = CharacterModelBpState.FromRoom(room, layoutJson)
            });
        });

        endpoints.MapPost("/api/character-model-3d/assets/import", async (
            HttpRequest request,
            ICharacterModel3DAssetService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var bodySizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (bodySizeFeature is { IsReadOnly: false })
                {
                    bodySizeFeature.MaxRequestBodySize = 1024L * 1024L * 1024L;
                }

                var category = request.Form["category"].FirstOrDefault() ?? "assets";
                var primaryName = request.Form["primaryName"].FirstOrDefault();
                return Results.Ok(await service.ImportAsync(request.Form.Files.ToArray(), category, primaryName, cancellationToken));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        }).DisableAntiforgery();
    }

    /// <summary>
    /// 角色模型 BP 状态内部类，用于聚合房间角色选择与禁用数据。
    /// </summary>
    private sealed class CharacterModelBpState
    {
        public string RoomId { get; init; } = "";
        public string?[] Survivors { get; init; } = [null, null, null, null];
        public string? Hunter { get; init; }
        public string[] HunterBannedSurvivors { get; init; } = [];
        public string[] SurvivorBannedHunters { get; init; } = [];
        public string[] GlobalBannedSurvivors { get; init; } = [];
        public string[] GlobalBannedHunters { get; init; } = [];
        public JsonElement? CharacterModel3DLayout { get; init; }

        /// <summary>
        /// 从房间数据构建角色模型 BP 状态。
        /// </summary>
        public static CharacterModelBpState FromRoom(BpRoom room, string? layoutJson)
        {
            return new CharacterModelBpState
            {
                RoomId = room.RoomId,
                Survivors =
                [
                    CharacterKey(room.CharacterPicks.Survivor1),
                    CharacterKey(room.CharacterPicks.Survivor2),
                    CharacterKey(room.CharacterPicks.Survivor3),
                    CharacterKey(room.CharacterPicks.Survivor4)
                ],
                Hunter = CharacterKey(room.CharacterPicks.Hunter),
                HunterBannedSurvivors = room.Bans.SurvivorBans.Select(x => x.CharacterId).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
                SurvivorBannedHunters = room.Bans.HunterBans.Select(x => x.CharacterId).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
                GlobalBannedSurvivors = room.GlobalBans.SurvivorBans.Select(x => x.CharacterId).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
                GlobalBannedHunters = room.GlobalBans.HunterBans.Select(x => x.CharacterId).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
                CharacterModel3DLayout = TryParseLayout(layoutJson)
            };
        }

        /// <summary>
        /// 从玩家数据提取角色键值。
        /// </summary>
        private static string? CharacterKey(Player player)
            => string.IsNullOrWhiteSpace(player.CharacterId) ? null : player.CharacterId.Trim();

        /// <summary>
        /// 尝试解析布局 JSON 字符串。
        /// </summary>
        public static JsonElement? TryParseLayout(string? layoutJson)
        {
            if (string.IsNullOrWhiteSpace(layoutJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(layoutJson);
                return document.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }
    }
}
