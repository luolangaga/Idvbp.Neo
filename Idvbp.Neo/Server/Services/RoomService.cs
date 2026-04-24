using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Idvbp.Neo.Models;
using Idvbp.Neo.Models.Enums;
using Idvbp.Neo.Server.Contracts;

namespace Idvbp.Neo.Server.Services;

public interface IRoomService
{
    Task<IReadOnlyCollection<BpRoom>> GetRoomsAsync(CancellationToken cancellationToken = default);
    Task<BpRoom?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default);
    Task<BpRoom> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default);
    Task<BpRoom> CreateMatchAsync(string roomId, CreateMatchRequest request, CancellationToken cancellationToken = default);
    Task<BpRoom> UpdateMapAsync(string roomId, UpdateMapRequest request, CancellationToken cancellationToken = default);
    Task<BpRoom> AddBanAsync(string roomId, AddBanRequest request, CancellationToken cancellationToken = default);
    Task<BpRoom> AddGlobalBanAsync(string roomId, AddBanRequest request, CancellationToken cancellationToken = default);
    Task<BpRoom> SelectRoleAsync(string roomId, SelectRoleRequest request, CancellationToken cancellationToken = default);
    Task<BpRoom> UpdatePhaseAsync(string roomId, UpdatePhaseRequest request, CancellationToken cancellationToken = default);
}

public sealed class RoomService : IRoomService
{
    private readonly IRoomRepository _repository;
    private readonly IRoomEventPublisher _eventPublisher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RoomService(IRoomRepository repository, IRoomEventPublisher eventPublisher)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
    }

    public Task<IReadOnlyCollection<BpRoom>> GetRoomsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_repository.GetAll());

    public Task<BpRoom?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default)
        => Task.FromResult(_repository.GetById(roomId));

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
                    BanSlotsPerSide = Math.Max(0, request.MapBanSlotsPerSide)
                },
                Bans = new BanSelection
                {
                    SurvivorBanSlots = Math.Max(0, request.SurvivorBanSlots),
                    HunterBanSlots = Math.Max(0, request.HunterBanSlots)
                },
                GlobalBans = new GlobalBanSelection
                {
                    SurvivorBanSlots = Math.Max(0, request.GlobalSurvivorBanSlots),
                    HunterBanSlots = Math.Max(0, request.GlobalHunterBanSlots)
                }
            };

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
            var mapBanSlotsPerSide = room.MapSelection.BanSlotsPerSide;
            var survivorBanSlots = room.Bans.SurvivorBanSlots;
            var hunterBanSlots = room.Bans.HunterBanSlots;
            var globalSurvivorBanSlots = room.GlobalBans.SurvivorBanSlots;
            var globalHunterBanSlots = room.GlobalBans.HunterBanSlots;

            room.StartNewRound();
            room.MapSelection.BanSlotsPerSide = mapBanSlotsPerSide;
            room.Bans.SurvivorBanSlots = survivorBanSlots;
            room.Bans.HunterBanSlots = hunterBanSlots;
            if (request.ResetGlobalBans)
            {
                room.GlobalBans = new GlobalBanSelection
                {
                    SurvivorBanSlots = globalSurvivorBanSlots,
                    HunterBanSlots = globalHunterBanSlots
                };
            }

            if (request.CurrentPhase.HasValue)
            {
                room.CurrentPhase = request.CurrentPhase.Value;
            }

            room.Touch();
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
            room.CurrentPhase = request.Phase;
            room.Touch();
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
            if (isGlobalBan)
            {
                var bans = request.Role == CharacterRole.Survivor ? room.GlobalBans.SurvivorBans : room.GlobalBans.HunterBans;
                var slots = request.Role == CharacterRole.Survivor ? room.GlobalBans.SurvivorBanSlots : room.GlobalBans.HunterBanSlots;
                AddBanEntry(bans.Select(x => x.CharacterId), slots, bans, request.CharacterId.Trim(), request.Order);
                room.Touch();
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
                var slots = request.Role == CharacterRole.Survivor ? room.Bans.SurvivorBanSlots : room.Bans.HunterBanSlots;
                AddBanEntry(bans.Select(x => x.CharacterId), slots, bans, request.CharacterId.Trim(), request.Order);
                room.Touch();
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

    private BpRoom GetRequiredRoom(string roomId)
        => _repository.GetById(roomId) ?? throw new KeyNotFoundException($"Room '{roomId}' was not found.");

    private static void AddBanEntry(IEnumerable<string> existingIds, int slots, System.Collections.ObjectModel.ObservableCollection<PickBanEntry> bans, string characterId, int? order)
    {
        if (existingIds.Contains(characterId, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Character '{characterId}' has already been banned.");
        }

        if (slots > 0 && bans.Count >= slots)
        {
            throw new InvalidOperationException("No ban slots remaining.");
        }

        bans.Add(new PickBanEntry(characterId, order ?? bans.Count + 1));
    }

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
