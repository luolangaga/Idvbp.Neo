namespace Idvbp.Neo.Server.Contracts;

public static class RoomEventNames
{
    public const string RoomSnapshot = "room.snapshot";
    public const string RoomInfoUpdated = "room.info.updated";
    public const string MatchCreated = "match.created";
    public const string MapUpdated = "room.map.updated";
    public const string BanUpdated = "room.ban.updated";
    public const string GlobalBanUpdated = "room.global-ban.updated";
    public const string RoleSelected = "room.role.selected";
    public const string PhaseUpdated = "room.phase.updated";

    public static readonly string[] All =
    [
        RoomSnapshot,
        RoomInfoUpdated,
        MatchCreated,
        MapUpdated,
        BanUpdated,
        GlobalBanUpdated,
        RoleSelected,
        PhaseUpdated
    ];
}
