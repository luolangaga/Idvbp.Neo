using System.Collections.Generic;
using System;
using System.Text.Json;
using Idvbp.Neo.Models;
using Idvbp.Neo.Models.Enums;

namespace Idvbp.Neo.Server.Contracts;

/// <summary>
/// 创建房间请求。
/// </summary>
public class CreateRoomRequest
{
    public string? RoomId { get; init; }
    public string RoomName { get; init; } = string.Empty;
    public string TeamAName { get; init; } = "Team A";
    public string TeamBName { get; init; } = "Team B";
    public int MapBanSlotsPerSide { get; init; }
    public int SurvivorBanSlots { get; init; }
    public int HunterBanSlots { get; init; }
    public int GlobalSurvivorBanSlots { get; init; }
    public int GlobalHunterBanSlots { get; init; }
}

/// <summary>
/// 创建对局请求。
/// </summary>
public class CreateMatchRequest
{
    public int? TargetRound { get; init; }
    public BpPhase? CurrentPhase { get; init; }
    public bool ResetGlobalBans { get; init; }
}

/// <summary>
/// 更新地图请求。
/// </summary>
public class UpdateMapRequest
{
    public string MapId { get; init; } = string.Empty;
    public string? MapName { get; init; }
    public string? ImageUrl { get; init; }
    public BpPhase? NextPhase { get; init; }
}

/// <summary>
/// Add a map ban to the current room.
/// </summary>
public class AddMapBanRequest
{
    public string MapId { get; init; } = string.Empty;
    public int? Order { get; init; }
}

/// <summary>
/// 添加禁用请求。
/// </summary>
public class AddBanRequest
{
    public CharacterRole Role { get; init; }
    public string CharacterId { get; init; } = string.Empty;
    public int? Order { get; init; }
}

/// <summary>
/// 选择角色请求。
/// </summary>
public class SelectRoleRequest
{
    public string Slot { get; init; } = string.Empty;
    public string PlayerId { get; init; } = string.Empty;
    public string PlayerName { get; init; } = string.Empty;
    public string TeamId { get; init; } = string.Empty;
    public string CharacterId { get; init; } = string.Empty;
}

/// <summary>
/// 更新阶段请求。
/// </summary>
public class UpdatePhaseRequest
{
    public BpPhase Phase { get; init; }
}

/// <summary>
/// 更新房间队伍请求。
/// </summary>
public class UpdateRoomTeamsRequest
{
    public UpdateTeamRequest TeamA { get; init; } = new();
    public UpdateTeamRequest TeamB { get; init; } = new();
}

/// <summary>
/// 更新队伍请求。
/// </summary>
public class UpdateTeamRequest
{
    public string Name { get; init; } = string.Empty;
    public byte[]? LogoData { get; init; }
    public IReadOnlyList<UpdateTeamPlayerRequest> Members { get; init; } = [];
}

/// <summary>
/// 更新队伍选手请求。
/// </summary>
public class UpdateTeamPlayerRequest
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// 地图更新事件载荷。
/// </summary>
public class MapUpdatedPayload
{
    public int CurrentRound { get; init; }
    public MapSelection MapSelection { get; init; } = new();
}

/// <summary>
/// 禁用更新事件载荷。
/// </summary>
public class BanUpdatedPayload
{
    public int CurrentRound { get; init; }
    public CharacterRole Role { get; init; }
    public BanSelection Bans { get; init; } = new();
}

/// <summary>
/// 全局禁用更新事件载荷。
/// </summary>
public class GlobalBanUpdatedPayload
{
    public CharacterRole Role { get; init; }
    public GlobalBanSelection GlobalBans { get; init; } = new();
}

/// <summary>
/// 角色选择事件载荷。
/// </summary>
public class RoleSelectedPayload
{
    public string Slot { get; init; } = string.Empty;
    public Player Player { get; init; } = new();
    public CharacterPickSelection CharacterPicks { get; init; } = new();
}

/// <summary>
/// 阶段更新事件载荷。
/// </summary>
public class PhaseUpdatedPayload
{
    public BpPhase Phase { get; init; }
}

public class SetCurrentRoomRequest
{
    public string? RoomId { get; init; }
}

public class CurrentRoomPayload
{
    public string? RoomId { get; init; }
    public string? RoomName { get; init; }
    public int? CurrentRound { get; init; }
    public string? CurrentPhase { get; init; }
    public JsonElement? Room { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 房间列表摘要（不含 logo 图片数据等大字段）。
/// </summary>
public class RoomSummary
{
    public string RoomId { get; init; } = string.Empty;
    public string RoomName { get; init; } = string.Empty;
    public string CurrentPhase { get; init; } = string.Empty;
    public int CurrentRound { get; init; }
    public TeamSummary TeamA { get; init; } = new();
    public TeamSummary TeamB { get; init; } = new();
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>
/// 队伍摘要（不含 LogoData 二进制数据）。
/// </summary>
public class TeamSummary
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
}
