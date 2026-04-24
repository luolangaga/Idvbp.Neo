using Idvbp.Neo.Models;
using Idvbp.Neo.Models.Enums;

namespace Idvbp.Neo.Server.Contracts;

public class CreateRoomRequest
{
    public string? RoomId { get; init; }
    public string RoomName { get; init; } = string.Empty;
    public string TeamAName { get; init; } = "Team A";
    public string TeamBName { get; init; } = "Team B";
    public int MapBanSlotsPerSide { get; init; } = 1;
    public int SurvivorBanSlots { get; init; } = 1;
    public int HunterBanSlots { get; init; } = 1;
    public int GlobalSurvivorBanSlots { get; init; } = 1;
    public int GlobalHunterBanSlots { get; init; } = 1;
}

public class CreateMatchRequest
{
    public BpPhase? CurrentPhase { get; init; }
    public bool ResetGlobalBans { get; init; }
}

public class UpdateMapRequest
{
    public string MapId { get; init; } = string.Empty;
    public string? MapName { get; init; }
    public string? ImageUrl { get; init; }
    public BpPhase? NextPhase { get; init; }
}

public class AddBanRequest
{
    public CharacterRole Role { get; init; }
    public string CharacterId { get; init; } = string.Empty;
    public int? Order { get; init; }
}

public class SelectRoleRequest
{
    public string Slot { get; init; } = string.Empty;
    public string PlayerId { get; init; } = string.Empty;
    public string PlayerName { get; init; } = string.Empty;
    public string TeamId { get; init; } = string.Empty;
    public string CharacterId { get; init; } = string.Empty;
}

public class UpdatePhaseRequest
{
    public BpPhase Phase { get; init; }
}

public class MapUpdatedPayload
{
    public int CurrentRound { get; init; }
    public MapSelection MapSelection { get; init; } = new();
}

public class BanUpdatedPayload
{
    public int CurrentRound { get; init; }
    public CharacterRole Role { get; init; }
    public BanSelection Bans { get; init; } = new();
}

public class GlobalBanUpdatedPayload
{
    public CharacterRole Role { get; init; }
    public GlobalBanSelection GlobalBans { get; init; } = new();
}

public class RoleSelectedPayload
{
    public string Slot { get; init; } = string.Empty;
    public Player Player { get; init; } = new();
    public CharacterPickSelection CharacterPicks { get; init; } = new();
}

public class PhaseUpdatedPayload
{
    public BpPhase Phase { get; init; }
}
