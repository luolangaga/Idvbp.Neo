using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Idvbp.Neo.Models;
using Idvbp.Neo.Models.Enums;
using Idvbp.Neo.Server.Contracts;

namespace Idvbp.Neo.Server.Services;

/// <summary>
/// 房间服务接口，定义房间生命周期与状态变更操作。
/// </summary>
public interface IRoomService
{
    /// <summary>
    /// 获取所有房间列表。
    /// </summary>
    Task<IReadOnlyCollection<BpRoom>> GetRoomsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据 ID 获取指定房间。
    /// </summary>
    Task<BpRoom?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建新房间。
    /// </summary>
    Task<BpRoom> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 为指定房间创建新对局。
    /// </summary>
    Task<BpRoom> CreateMatchAsync(string roomId, CreateMatchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新房间地图选择。
    /// </summary>
    Task<BpRoom> UpdateMapAsync(string roomId, UpdateMapRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 娣诲姞鍦板浘绂佺敤銆?
    /// </summary>
    Task<BpRoom> AddMapBanAsync(string roomId, AddMapBanRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加角色禁用（单局）。
    /// </summary>
    Task<BpRoom> AddBanAsync(string roomId, AddBanRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加全局角色禁用。
    /// </summary>
    Task<BpRoom> AddGlobalBanAsync(string roomId, AddBanRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 选择角色（选手与角色绑定）。
    /// </summary>
    Task<BpRoom> SelectRoleAsync(string roomId, SelectRoleRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新房间当前阶段。
    /// </summary>
    Task<BpRoom> UpdatePhaseAsync(string roomId, UpdatePhaseRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新房间队伍信息。
    /// </summary>
    Task<BpRoom> UpdateTeamsAsync(string roomId, UpdateRoomTeamsRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 房间服务实现，处理房间数据的持久化与实时事件发布。
/// </summary>
public sealed class RoomService : IRoomService
{
    private readonly IRoomRepository _repository;
    private readonly IRoomEventPublisher _eventPublisher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 初始化房间服务。
    /// </summary>
    /// <param name="repository">房间仓储。</param>
    /// <param name="eventPublisher">房间事件发布器。</param>
    public RoomService(IRoomRepository repository, IRoomEventPublisher eventPublisher)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// 获取所有房间列表。
    /// </summary>
    public Task<IReadOnlyCollection<BpRoom>> GetRoomsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_repository.GetAll());

    /// <summary>
    /// 根据 ID 获取指定房间。
    /// </summary>
    public Task<BpRoom?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default)
        => Task.FromResult(_repository.GetById(roomId));

    /// <summary>
    /// 创建新房间并初始化默认队伍与规则配置。
    /// </summary>
    public async Task<BpRoom> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RoomName))
        {
            throw new ArgumentException("RoomName is required.", nameof(request));
        }

        var roomId = string.IsNullOrWhiteSpace(request.RoomId) ? Guid.NewGuid().ToString("N") : request.RoomId.Trim();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_repository.Exists(roomId))
            {
                throw new InvalidOperationException($"Room '{roomId}' already exists.");
            }

            var now = DateTimeOffset.UtcNow;
            var room = new BpRoom
            {
                RoomId = roomId,
                RoomName = request.RoomName.Trim(),
                CurrentPhase = BpPhase.Waiting,
                CurrentRound = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                TeamA = new Team
                {
                    Id = $"{roomId}-team-a",
                    Name = request.TeamAName.Trim(),
                    CurrentSide = GameSide.Survivor
                },
                TeamB = new Team
                {
                    Id = $"{roomId}-team-b",
                    Name = request.TeamBName.Trim(),
                    CurrentSide = GameSide.Hunter
                },
                MapSelection = new MapSelection
                {
                    BanSlotsPerSide = 0
                },
                Bans = new BanSelection
                {
                    SurvivorBanSlots = 0,
                    HunterBanSlots = 0
                },
                GlobalBans = new GlobalBanSelection
                {
                    SurvivorBanSlots = 0,
                    HunterBanSlots = 0
                }
            };
            room.EnsureRoundState(1);

            _repository.Upsert(room);
            await _eventPublisher.PublishAsync(room.RoomId, RoomEventNames.RoomInfoUpdated, room);
            return room;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BpRoom> CreateMatchAsync(string roomId, CreateMatchRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var room = GetRequiredRoom(roomId);
            var targetRound = request.TargetRound.GetValueOrDefault(room.CurrentRound + 1);
            if (targetRound <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "TargetRound must be greater than 0.");
            }

            room.SwitchToRound(targetRound, request.CurrentPhase ?? BpPhase.Waiting, request.ResetGlobalBans);
            _repository.Upsert(room);
            await _eventPublisher.PublishAsync(room.RoomId, RoomEventNames.MatchCreated, room);
            return room;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BpRoom> UpdateMapAsync(string roomId, UpdateMapRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.MapId))
        {
            throw new ArgumentException("MapId is required.", nameof(request));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var room = GetRequiredRoom(roomId);
            room.EnsureRoundState(room.CurrentRound);
            room.MapSelection.PickedMap = new MapInfo
            {
                Id = request.MapId.Trim(),
                Name = string.IsNullOrWhiteSpace(request.MapName) ? request.MapId.Trim() : request.MapName.Trim(),
                ImageUrl = request.ImageUrl
            };

            if (request.NextPhase.HasValue)
            {
                room.CurrentPhase = request.NextPhase.Value;
            }

            room.Touch();
            room.StoreCurrentRoundState();
            _repository.Upsert(room);
            await _eventPublisher.PublishAsync(room.RoomId, RoomEventNames.MapUpdated, new MapUpdatedPayload
            {
                CurrentRound = room.CurrentRound,
                MapSelection = room.MapSelection
            });

            return room;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BpRoom> AddMapBanAsync(string roomId, AddMapBanRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.MapId))
        {
            throw new ArgumentException("MapId is required.", nameof(request));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var room = GetRequiredRoom(roomId);
            room.EnsureRoundState(room.CurrentRound);
            var mapId = request.MapId.Trim();
            if (room.MapSelection.BannedMaps.Any(x => string.Equals(x.MapId, mapId, StringComparison.OrdinalIgnoreCase)))
            {
                return room;
            }

            room.MapSelection.BannedMaps.Add(new MapBanEntry
            {
                MapId = mapId,
                Order = request.Order ?? room.MapSelection.BannedMaps.Count + 1
            });
            room.Touch();
            room.StoreCurrentRoundState();
            _repository.Upsert(room);
            await _eventPublisher.PublishAsync(room.RoomId, RoomEventNames.MapUpdated, new MapUpdatedPayload
            {
                CurrentRound = room.CurrentRound,
                MapSelection = room.MapSelection
            });

            return room;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<BpRoom> AddBanAsync(string roomId, AddBanRequest request, CancellationToken cancellationToken = default)
        => AddBanCoreAsync(roomId, request, isGlobalBan: false, cancellationToken);

    public Task<BpRoom> AddGlobalBanAsync(string roomId, AddBanRequest request, CancellationToken cancellationToken = default)
        => AddBanCoreAsync(roomId, request, isGlobalBan: true, cancellationToken);

    public async Task<BpRoom> SelectRoleAsync(string roomId, SelectRoleRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Slot))
        {
            throw new ArgumentException("Slot is required.", nameof(request));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var room = GetRequiredRoom(roomId);
            room.EnsureRoundState(room.CurrentRound);
            var slot = request.Slot.Trim();
            var seat = ResolveSeat(slot);
            var player = new Player
            {
                Id = request.PlayerId.Trim(),
                Name = request.PlayerName.Trim(),
                TeamId = request.TeamId.Trim(),
                CharacterId = request.CharacterId.Trim(),
                SeatNumber = seat
            };

            switch (slot.ToLowerInvariant())
            {
                case "survivor1":
                    room.CharacterPicks.Survivor1 = player;
                    break;
                case "survivor2":
                    room.CharacterPicks.Survivor2 = player;
                    break;
                case "survivor3":
                    room.CharacterPicks.Survivor3 = player;
                    break;
                case "survivor4":
                    room.CharacterPicks.Survivor4 = player;
                    break;
                case "hunter":
                    room.CharacterPicks.Hunter = player;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported slot '{request.Slot}'.");
            }

            room.Touch();
            room.StoreCurrentRoundState();
            _repository.Upsert(room);
            await _eventPublisher.PublishAsync(room.RoomId, RoomEventNames.RoleSelected, new RoleSelectedPayload
            {
                Slot = slot,
                Player = player,
                CharacterPicks = room.CharacterPicks
            });

            return room;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BpRoom> UpdatePhaseAsync(string roomId, UpdatePhaseRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var room = GetRequiredRoom(roomId);
            room.EnsureRoundState(room.CurrentRound);
            room.CurrentPhase = request.Phase;
            room.Touch();
            room.StoreCurrentRoundState();
            _repository.Upsert(room);
            await _eventPublisher.PublishAsync(room.RoomId, RoomEventNames.PhaseUpdated, new PhaseUpdatedPayload
            {
                Phase = room.CurrentPhase
            });

            return room;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BpRoom> UpdateTeamsAsync(string roomId, UpdateRoomTeamsRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var room = GetRequiredRoom(roomId);
            room.EnsureRoundState(room.CurrentRound);
            ApplyTeamUpdate(room.TeamA, request.TeamA);
            ApplyTeamUpdate(room.TeamB, request.TeamB);
            room.StoreCurrentRoundState();
            room.Touch();
            _repository.Upsert(room);
            await _eventPublisher.PublishAsync(room.RoomId, RoomEventNames.RoomInfoUpdated, room);
            return room;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BpRoom> AddBanCoreAsync(string roomId, AddBanRequest request, bool isGlobalBan, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CharacterId))
        {
            throw new ArgumentException("CharacterId is required.", nameof(request));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var room = GetRequiredRoom(roomId);
            room.EnsureRoundState(room.CurrentRound);
            if (isGlobalBan)
            {
                var bans = request.Role == CharacterRole.Survivor ? room.GlobalBans.SurvivorBans : room.GlobalBans.HunterBans;
                UpsertBanEntry(bans, request.CharacterId.Trim(), request.Order);
                room.Touch();
                room.StoreCurrentRoundState();
                _repository.Upsert(room);
                await _eventPublisher.PublishAsync(room.RoomId, RoomEventNames.GlobalBanUpdated, new GlobalBanUpdatedPayload
                {
                    Role = request.Role,
                    GlobalBans = room.GlobalBans
                });
            }
            else
            {
                var bans = request.Role == CharacterRole.Survivor ? room.Bans.SurvivorBans : room.Bans.HunterBans;
                UpsertBanEntry(bans, request.CharacterId.Trim(), request.Order);
                room.Touch();
                room.StoreCurrentRoundState();
                _repository.Upsert(room);
                await _eventPublisher.PublishAsync(room.RoomId, RoomEventNames.BanUpdated, new BanUpdatedPayload
                {
                    CurrentRound = room.CurrentRound,
                    Role = request.Role,
                    Bans = room.Bans
                });
            }

            return room;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 获取指定房间，若不存在则抛出异常。
    /// </summary>
    /// <param name="roomId">房间标识。</param>
    /// <returns>房间实例。</returns>
    private BpRoom GetRequiredRoom(string roomId)
        => _repository.GetById(roomId) ?? throw new KeyNotFoundException($"Room '{roomId}' was not found.");

    /// <summary>
    /// 将队伍更新请求应用到队伍实体。
    /// </summary>
    /// <param name="team">目标队伍。</param>
    /// <param name="request">更新请求。</param>
    private static void ApplyTeamUpdate(Team team, UpdateTeamRequest request)
    {
        team.Name = string.IsNullOrWhiteSpace(request.Name) ? team.Name : request.Name.Trim();
        team.LogoData = request.LogoData;
        team.Members = new System.Collections.ObjectModel.ObservableCollection<Player>(request.Members
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select((x, index) => new Player
            {
                Id = string.IsNullOrWhiteSpace(x.Id) ? $"{team.Id}-member-{index + 1}" : x.Id.Trim(),
                Name = x.Name.Trim(),
                TeamId = team.Id,
                SeatNumber = index + 1
            }));
    }

    /// <summary>
    /// 向禁用列表添加条目，检查重复并按顺序写入。
    /// </summary>
    /// <param name="bans">禁用条目集合。</param>
    /// <param name="characterId">要禁用的角色 ID。</param>
    /// <param name="order">禁用顺序（可选）。</param>
    private static void UpsertBanEntry(System.Collections.ObjectModel.ObservableCollection<PickBanEntry> bans, string characterId, int? order)
    {
        var normalizedOrder = order ?? bans.Count + 1;
        if (normalizedOrder <= 0)
        {
            throw new ArgumentException("Ban order must be greater than zero.", nameof(order));
        }

        var duplicate = bans.FirstOrDefault(x =>
            string.Equals(x.CharacterId, characterId, StringComparison.OrdinalIgnoreCase)
            && x.Order != normalizedOrder);
        if (duplicate is not null)
        {
            return;
        }

        var existingSlot = bans.FirstOrDefault(x => x.Order == normalizedOrder);
        if (existingSlot is not null)
        {
            existingSlot.CharacterId = characterId;
            return;
        }

        bans.Add(new PickBanEntry(characterId, normalizedOrder));
    }

    /// <summary>
    /// 根据槽位名称解析座位号。
    /// </summary>
    /// <param name="slot">槽位名称。</param>
    /// <returns>座位号。</returns>
    private static int ResolveSeat(string slot) => slot.ToLowerInvariant() switch
    {
        "survivor1" => 1,
        "survivor2" => 2,
        "survivor3" => 3,
        "survivor4" => 4,
        "hunter" => 1,
        _ => throw new InvalidOperationException($"Unsupported slot '{slot}'.")
    };
}
