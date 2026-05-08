# Current Room Sync API

This API lets the desktop BP page publish the currently selected room, so browser frontends can connect to the right room without hardcoding `roomId` in the URL.

## REST

### Get current room

`GET /api/rooms/current`

Response:

```json
{
  "roomId": "room-001",
  "roomName": "IVL W1 A vs B",
  "currentRound": 1,
  "currentPhase": "Waiting",
  "room": {},
  "occurredAtUtc": "2026-05-07T10:00:00+00:00"
}
```

When no room has been selected yet, `roomId` and `room` are `null`.

### Set current room

`PUT /api/rooms/current`

Request:

```json
{
  "roomId": "room-001"
}
```

Response is the same as `GET /api/rooms/current`. Passing `null` or an empty `roomId` clears the current room. If the room does not exist, the API returns `404`.

## SignalR

Hub: `/hubs/game`

### Server-to-client event

`CurrentRoomChanged`

Payload:

```json
{
  "roomId": "room-001",
  "roomName": "IVL W1 A vs B",
  "currentRound": 1,
  "currentPhase": "Waiting",
  "room": {},
  "occurredAtUtc": "2026-05-07T10:00:00+00:00"
}
```

Browser pages should listen to this event, leave their old room subscription, join the new `roomId`, replace room event subscriptions, then request a snapshot.

### Client-to-server operations

`RequestCurrentRoom()`

Returns the same payload as `GET /api/rooms/current`.

`SetCurrentRoom(roomId)`

Sets the globally selected room and broadcasts `CurrentRoomChanged` to all connected clients.

## Browser flow

1. Connect to `/hubs/game`.
2. Listen for `CurrentRoomChanged`.
3. Call `RequestCurrentRoom()`.
4. If a `roomId` is returned, call:

```js
await connection.invoke("JoinRoom", roomId);
await connection.invoke("ReplaceSubscriptions", roomId, [
  "room.snapshot",
  "room.info.updated",
  "match.created",
  "room.map.updated",
  "room.ban.updated",
  "room.global-ban.updated",
  "room.role.selected",
  "room.phase.updated"
]);
await connection.invoke("RequestRoomSnapshot", roomId);
```

The bundled layout runtime at `/bp-layout` now performs this automatically when the URL does not specify `roomId`.
