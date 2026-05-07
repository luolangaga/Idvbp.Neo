using System;
using System.Collections.Generic;
using System.Threading;
using Idvbp.Neo.Server.Contracts;
using Idvbp.Neo.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Idvbp.Neo.Server;

/// <summary>
/// BP 房间相关 API 端点定义。
/// </summary>
public static class BpApiEndpoints
{
    /// <summary>
    /// 映射 BP 房间相关的 REST API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    public static void MapBpApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/rooms", async (IRoomService roomService, CancellationToken cancellationToken) =>
            Results.Ok(await roomService.GetRoomsAsync(cancellationToken)));

        endpoints.MapGet("/api/rooms/{roomId}", async (string roomId, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            var room = await roomService.GetRoomAsync(roomId, cancellationToken);
            return room is null ? Results.NotFound(new { message = $"Room '{roomId}' not found." }) : Results.Ok(room);
        });

        endpoints.MapGet("/api/rooms/current", async (ICurrentRoomStateService currentRoomStateService, CancellationToken cancellationToken) =>
            Results.Ok(await currentRoomStateService.GetCurrentRoomAsync(cancellationToken)));

        endpoints.MapPut("/api/rooms/current", async (SetCurrentRoomRequest request, ICurrentRoomStateService currentRoomStateService, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await currentRoomStateService.SetCurrentRoomAsync(request.RoomId, cancellationToken));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { message = exception.Message });
            }
        });

        endpoints.MapPost("/api/rooms", async (CreateRoomRequest request, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            try
            {
                var room = await roomService.CreateRoomAsync(request, cancellationToken);
                return Results.Created($"/api/rooms/{room.RoomId}", room);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        endpoints.MapPost("/api/rooms/{roomId}/matches", async (string roomId, CreateMatchRequest request, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await roomService.CreateMatchAsync(roomId, request, cancellationToken));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { message = exception.Message });
            }
        });

        endpoints.MapPatch("/api/rooms/{roomId}/map", async (string roomId, UpdateMapRequest request, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await roomService.UpdateMapAsync(roomId, request, cancellationToken));
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
            {
                return ToProblemResult(exception);
            }
        });

        endpoints.MapPost("/api/rooms/{roomId}/map-bans", async (string roomId, AddMapBanRequest request, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await roomService.AddMapBanAsync(roomId, request, cancellationToken));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return ToProblemResult(exception);
            }
        });

        endpoints.MapPost("/api/rooms/{roomId}/bans", async (string roomId, AddBanRequest request, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await roomService.AddBanAsync(roomId, request, cancellationToken));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return ToProblemResult(exception);
            }
        });

        endpoints.MapPost("/api/rooms/{roomId}/global-bans", async (string roomId, AddBanRequest request, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await roomService.AddGlobalBanAsync(roomId, request, cancellationToken));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return ToProblemResult(exception);
            }
        });

        endpoints.MapPost("/api/rooms/{roomId}/roles", async (string roomId, SelectRoleRequest request, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await roomService.SelectRoleAsync(roomId, request, cancellationToken));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return ToProblemResult(exception);
            }
        });

        endpoints.MapPatch("/api/rooms/{roomId}/phase", async (string roomId, UpdatePhaseRequest request, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await roomService.UpdatePhaseAsync(roomId, request, cancellationToken));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { message = exception.Message });
            }
        });

        endpoints.MapPatch("/api/rooms/{roomId}/teams", async (string roomId, UpdateRoomTeamsRequest request, IRoomService roomService, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await roomService.UpdateTeamsAsync(roomId, request, cancellationToken));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { message = exception.Message });
            }
        });

        endpoints.MapGet("/api/signalr/events", () => Results.Ok(RoomEventNames.All));
    }

    /// <summary>
    /// 将异常转换为对应的 HTTP 问题结果。
    /// </summary>
    /// <param name="exception">发生的异常。</param>
    /// <returns>对应的 HTTP 结果。</returns>
    private static IResult ToProblemResult(Exception exception)
        => exception switch
        {
            KeyNotFoundException => Results.NotFound(new { message = exception.Message }),
            _ => Results.BadRequest(new { message = exception.Message })
        };
}
