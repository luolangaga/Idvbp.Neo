# BP 房间 API / SignalR 对接文档

## 基础信息

- HTTP 基础地址：`http://localhost:5000`
- SignalR Hub：`/hubs/game`
- 健康检查：`GET /api/health`
- 事件方法名：`RoomEvent`

## 数据持久化

- 服务端已接入 LiteDB。
- 默认数据库文件：`data/idvbp-neo.db`
- 房间、当前回合 BP 状态、全局 Ban、选角、地图、比分会持久化到本地数据库。

## REST API

### 1. 新建 ROOM

`POST /api/rooms`

请求体：

```json
{
  "roomName": "IVL W1 A vs B",
  "teamAName": "Team A",
  "teamBName": "Team B",
  "mapBanSlotsPerSide": 1,
  "survivorBanSlots": 2,
  "hunterBanSlots": 2,
  "globalSurvivorBanSlots": 1,
  "globalHunterBanSlots": 1
}
```

返回：完整 `BpRoom`。

### 2. 获取全部 ROOM

`GET /api/rooms`

返回：`BpRoom[]`

### 3. 获取 ROOM 详情

`GET /api/rooms/{roomId}`

返回：完整 `BpRoom`

### 4. 新建对局 / 新开一轮

`POST /api/rooms/{roomId}/matches`

请求体：

```json
{
  "currentPhase": "GlobalBans",
  "resetGlobalBans": false
}
```

行为：

- `CurrentRound + 1`
- 双方 `CurrentSide` 互换
- 重置当前轮 `MapSelection`、`Bans`、`CharacterPicks`
- 可选重置 `GlobalBans`

### 5. 增加一个 Ban 位

`POST /api/rooms/{roomId}/bans`

请求体：

```json
{
  "role": "Survivor",
  "characterId": "mercenary",
  "order": 1
}
```

说明：

- `role=Survivor` 表示加到 `Bans.SurvivorBans`
- `role=Hunter` 表示加到 `Bans.HunterBans`
- 超出配置 Ban 槽位或重复 Ban 会返回 `400`

### 6. 增加一个全局 Ban 位

`POST /api/rooms/{roomId}/global-bans`

请求体与普通 Ban 相同。

说明：

- `role=Survivor` 表示加到 `GlobalBans.SurvivorBans`
- `role=Hunter` 表示加到 `GlobalBans.HunterBans`

### 7. 选择角色

`POST /api/rooms/{roomId}/roles`

请求体：

```json
{
  "slot": "Survivor1",
  "playerId": "player-001",
  "playerName": "Alice",
  "teamId": "team-a",
  "characterId": "priestess"
}
```

`slot` 支持：

- `Survivor1`
- `Survivor2`
- `Survivor3`
- `Survivor4`
- `Hunter`

### 8. 修改地图

`PATCH /api/rooms/{roomId}/map`

请求体：

```json
{
  "mapId": "red-church",
  "mapName": "Red Church",
  "imageUrl": "/maps/red-church.png",
  "nextPhase": "SideBans"
}
```

### 9. 修改房间阶段

`PATCH /api/rooms/{roomId}/phase`

请求体：

```json
{
  "phase": "CharacterPicks"
}
```

### 10. 获取可订阅事件列表

`GET /api/signalr/events`

返回：

```json
[
  "room.snapshot",
  "room.info.updated",
  "match.created",
  "room.map.updated",
  "room.ban.updated",
  "room.global-ban.updated",
  "room.role.selected",
  "room.phase.updated"
]
```

## SignalR 对接

### 1. 连接

前端连接地址：`http://localhost:5000/hubs/game`

### 2. Hub 方法

- `JoinRoom(roomId)`：加入房间组
- `LeaveRoom(roomId)`：离开房间组并移除该房间下所有事件订阅
- `ReplaceSubscriptions(roomId, eventTypes)`：覆盖式设置订阅事件
- `SubscribeToEvents(roomId, eventTypes)`：增量订阅
- `UnsubscribeFromEvents(roomId, eventTypes)`：取消部分订阅
- `GetAvailableEventTypes()`：获取支持的事件类型
- `RequestRoomSnapshot(roomId)`：主动拉一次当前房间完整快照

### 3. 服务端推送方法

所有业务事件都通过 `RoomEvent` 方法推送。

统一结构：

```json
{
  "eventType": "room.map.updated",
  "roomId": "room-001",
  "occurredAtUtc": "2026-04-24T09:30:00+00:00",
  "payload": {}
}
```

### 4. 可订阅事件及 payload

`room.snapshot`

- payload：完整 `BpRoom`

`room.info.updated`

- payload：完整 `BpRoom`

`match.created`

- payload：完整 `BpRoom`

`room.map.updated`

```json
{
  "currentRound": 2,
  "mapSelection": {
    "pickedMap": {
      "id": "red-church",
      "name": "Red Church",
      "imageUrl": "/maps/red-church.png"
    },
    "bannedMaps": [],
    "banSlotsPerSide": 1
  }
}
```

`room.ban.updated`

```json
{
  "currentRound": 2,
  "role": "Survivor",
  "bans": {
    "survivorBans": [
      {
        "characterId": "mercenary",
        "order": 1
      }
    ],
    "hunterBans": [],
    "survivorBanSlots": 2,
    "hunterBanSlots": 2
  }
}
```

`room.global-ban.updated`

```json
{
  "role": "Hunter",
  "globalBans": {
    "survivorBans": [],
    "hunterBans": [
      {
        "characterId": "opera-singer",
        "order": 1
      }
    ],
    "survivorBanSlots": 1,
    "hunterBanSlots": 1
  }
}
```

`room.role.selected`

```json
{
  "slot": "Survivor1",
  "player": {
    "id": "player-001",
    "name": "Alice",
    "teamId": "team-a",
    "seatNumber": 1,
    "characterId": "priestess"
  },
  "characterPicks": {}
}
```

`room.phase.updated`

```json
{
  "phase": "CharacterPicks"
}
```

## 推荐前端接入流程

1. 建立 SignalR 连接。
2. 调用 `JoinRoom(roomId)`。
3. 调用 `ReplaceSubscriptions(roomId, [...需要的事件])`。
4. 调用 `RequestRoomSnapshot(roomId)` 获取初始化全量状态。
5. 后续只处理已订阅的 `RoomEvent`。
6. 页面离开时调用 `LeaveRoom(roomId)`。

## C# 客户端辅助能力

`Client/SignalRClient.cs` 已增加：

- `JoinRoomAsync`
- `LeaveRoomAsync`
- `ReplaceSubscriptionsAsync`
- `SubscribeToEventsAsync`
- `UnsubscribeFromEventsAsync`
- `RequestRoomSnapshotAsync`
- `GetAvailableEventTypesAsync`
- `OnRoomEvent`

## 主要模型字段

`BpRoom`

- `roomId`
- `roomName`
- `currentPhase`
- `currentRound`
- `teamA`
- `teamB`
- `mapSelection`
- `characterPicks`
- `bans`
- `globalBans`
- `matchScore`
- `createdAtUtc`
- `updatedAtUtc`
