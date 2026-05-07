# Idvbp.Neo API 参考文档

## 基础信息

| 项目 | 值 |
|------|-----|
| HTTP 基础地址 | `http://localhost:5000` |
| 数据持久化 | LiteDB (`data/idvbp-neo.db`) |
| 内容类型 | `application/json; charset=utf-8` |
| 服务器 | Kestrel (ASP.NET Core) |
| CORS | 全放通（AllowAnyOrigin） |

---

## 一、健康检查

### `GET /api/health`

**响应示例：**

```json
{
  "status": "ok",
  "timestamp": "2026-05-07T07:34:46.000+00:00"
}
```

---

## 二、BP 房间 API

### 2.1 获取所有房间

`GET /api/rooms`

**参数：** 无

**返回：** `BpRoom[]`

**示例请求：**

```
GET http://localhost:5000/api/rooms
```

**成功响应 (200)：**

```json
[]
```

---

### 2.2 获取指定房间

`GET /api/rooms/{roomId}`

**参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| `roomId` | string (path) | 房间 ID |

**错误响应：**

| 状态码 | 说明 |
|--------|------|
| 404 | 房间不存在 |

**示例请求：**

```
GET http://localhost:5000/api/rooms/1a74a4b0865b498597ec59209de54449
```

**成功响应 (200)：** 同 [2.3 创建房间](#23-创建房间) 返回结构。

---

### 2.3 创建房间

`POST /api/rooms`

**请求体：**

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

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `roomId` | string | 否 | 自动生成 GUID | 自定义房间 ID |
| `roomName` | string | **是** | - | 房间名称 |
| `teamAName` | string | 否 | `"Team A"` | A 队名称 |
| `teamBName` | string | 否 | `"Team B"` | B 队名称 |
| `mapBanSlotsPerSide` | int | 否 | `1` | 每边地图 Ban 位数量 |
| `survivorBanSlots` | int | 否 | `1` | 求生者 Ban 位数量 |
| `hunterBanSlots` | int | 否 | `1` | 监管者 Ban 位数量 |
| `globalSurvivorBanSlots` | int | 否 | `1` | 全局求生者 Ban 位数量 |
| `globalHunterBanSlots` | int | 否 | `1` | 全局监管者 Ban 位数量 |

**成功响应 (201 Created)：**

```json
{
  "roomId": "1a74a4b0865b498597ec59209de54449",
  "roomName": "TEST IVL W1",
  "currentPhase": "Waiting",
  "currentRound": 1,
  "teamA": {
    "id": "1a74a4b0865b498597ec59209de54449-team-a",
    "name": "Team A",
    "logoUrl": null,
    "logoData": null,
    "members": [],
    "currentSide": "Survivor"
  },
  "teamB": {
    "id": "1a74a4b0865b498597ec59209de54449-team-b",
    "name": "Team B",
    "logoUrl": null,
    "logoData": null,
    "members": [],
    "currentSide": "Hunter"
  },
  "mapSelection": {
    "bannedMaps": [],
    "pickedMap": null,
    "banSlotsPerSide": 1
  },
  "characterPicks": {
    "survivor1": { "id": "", "name": "", "avatarUrl": null, "teamId": "", "seatNumber": 1, "characterId": "" },
    "survivor2": { "id": "", "name": "", "avatarUrl": null, "teamId": "", "seatNumber": 2, "characterId": "" },
    "survivor3": { "id": "", "name": "", "avatarUrl": null, "teamId": "", "seatNumber": 3, "characterId": "" },
    "survivor4": { "id": "", "name": "", "avatarUrl": null, "teamId": "", "seatNumber": 4, "characterId": "" },
    "hunter": { "id": "", "name": "", "avatarUrl": null, "teamId": "", "seatNumber": 1, "characterId": "" }
  },
  "bans": {
    "survivorBans": [],
    "hunterBans": [],
    "survivorBanSlots": 2,
    "hunterBanSlots": 2
  },
  "globalBans": {
    "survivorBans": [],
    "hunterBans": [],
    "survivorBanSlots": 1,
    "hunterBanSlots": 1
  },
  "matchScore": {
    "rounds": [],
    "survivorMatchScore": 0,
    "hunterMatchScore": 0,
    "totalRounds": 0,
    "matchWinner": null
  },
  "createdAtUtc": "2026-05-07T07:29:25.139+00:00",
  "updatedAtUtc": "2026-05-07T07:29:25.139+00:00"
}
```

**错误响应 (400)：**

```json
{ "message": "RoomName is required." }
```

---

### 2.4 创建新对局（新开一轮）

`POST /api/rooms/{roomId}/matches`

**请求体：**

```json
{
  "currentPhase": "GlobalBans",
  "resetGlobalBans": false
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `currentPhase` | string (BpPhase) | 否 | 新对局的初始阶段 |
| `resetGlobalBans` | bool | 否 | 是否重置全局 Ban |

**行为：**
- `currentRound += 1`
- 双方 `currentSide` 互换
- 重置 `mapSelection`、`bans`、`characterPicks`
- 可选重置 `globalBans`

**示例请求：**

```
POST http://localhost:5000/api/rooms/{roomId}/matches
Content-Type: application/json

{"currentPhase":"GlobalBans","resetGlobalBans":false}
```

**成功响应 (200)：**

```json
{
  "roomId": "1a74a4b0865b498597ec59209de54449",
  "currentRound": 2,
  "currentPhase": "GlobalBans",
  ...
}
```

**错误响应：**

| 状态码 | 说明 |
|--------|------|
| 404 | 房间不存在 |

---

### 2.5 添加禁用（Ban）

`POST /api/rooms/{roomId}/bans`

**请求体：**

```json
{
  "role": "Survivor",
  "characterId": "mercenary",
  "order": 1
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `role` | string (CharacterRole) | **是** | `"Survivor"` 或 `"Hunter"` |
| `characterId` | string | **是** | 角色 ID |
| `order` | int | 否 | Ban 顺序（默认追加） |

**错误响应：**

| 状态码 | 说明 |
|--------|------|
| 400 | 超出 Ban 槽位上限 / 重复 Ban |
| 404 | 房间不存在 |

**示例请求：**

```
POST http://localhost:5000/api/rooms/{roomId}/bans
Content-Type: application/json

{"role":"Survivor","characterId":"mercenary","order":1}
```

**成功响应 (200)：**

```json
{
  "roomId": "...",
  "bans": {
    "survivorBans": [
      { "characterId": "mercenary", "order": 1 }
    ],
    "hunterBans": [],
    "survivorBanSlots": 2,
    "hunterBanSlots": 2
  },
  ...
}
```

---

### 2.6 添加全局禁用（Global Ban）

`POST /api/rooms/{roomId}/global-bans`

**请求体：** 与 [2.5 添加禁用](#25-添加禁用ban) 相同。

**说明：** `role=Survivor` 加入 `globalBans.survivorBans`；`role=Hunter` 加入 `globalBans.hunterBans`。

**示例请求：**

```
POST http://localhost:5000/api/rooms/{roomId}/global-bans
Content-Type: application/json

{"role":"Hunter","characterId":"opera-singer","order":1}
```

**成功响应 (200)：**

```json
{
  "roomId": "...",
  "globalBans": {
    "survivorBans": [],
    "hunterBans": [
      { "characterId": "opera-singer", "order": 1 }
    ],
    "survivorBanSlots": 1,
    "hunterBanSlots": 1
  },
  ...
}
```

---

### 2.7 选择角色

`POST /api/rooms/{roomId}/roles`

**请求体：**

```json
{
  "slot": "Survivor1",
  "playerId": "player-001",
  "playerName": "Alice",
  "teamId": "team-a",
  "characterId": "priestess"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `slot` | string | **是** | `Survivor1` / `Survivor2` / `Survivor3` / `Survivor4` / `Hunter` |
| `playerId` | string | 否 | 选手 ID |
| `playerName` | string | 否 | 选手名称 |
| `teamId` | string | 否 | 队伍 ID |
| `characterId` | string | 否 | 角色 ID |

**示例请求：**

```
POST http://localhost:5000/api/rooms/{roomId}/roles
Content-Type: application/json

{"slot":"Survivor1","playerId":"player-001","playerName":"Alice","teamId":"team-a","characterId":"priestess"}
```

**成功响应 (200)：**

```json
{
  "roomId": "...",
  "characterPicks": {
    "survivor1": {
      "id": "player-001",
      "name": "Alice",
      "avatarUrl": null,
      "teamId": "team-a",
      "seatNumber": 1,
      "characterId": "priestess"
    },
    ...
  },
  ...
}
```

---

### 2.8 修改地图

`PATCH /api/rooms/{roomId}/map`

**请求体：**

```json
{
  "mapId": "red-church",
  "mapName": "Red Church",
  "imageUrl": "/maps/red-church.png",
  "nextPhase": "SideBans"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `mapId` | string | **是** | 地图 ID |
| `mapName` | string | 否 | 地图显示名称 |
| `imageUrl` | string | 否 | 地图图片 URL |
| `nextPhase` | string (BpPhase) | 否 | 更新后切换的阶段 |

**示例请求：**

```
PATCH http://localhost:5000/api/rooms/{roomId}/map
Content-Type: application/json

{"mapId":"red-church","mapName":"Red Church","imageUrl":"/maps/red-church.png","nextPhase":"SideBans"}
```

**成功响应 (200)：**

```json
{
  "roomId": "...",
  "currentPhase": "SideBans",
  "mapSelection": {
    "pickedMap": {
      "id": "red-church",
      "name": "Red Church",
      "imageUrl": "/maps/red-church.png"
    },
    "bannedMaps": [],
    "banSlotsPerSide": 1
  },
  ...
}
```

---

### 2.9 修改房间阶段

`PATCH /api/rooms/{roomId}/phase`

**请求体：**

```json
{
  "phase": "CharacterPicks"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `phase` | string (BpPhase) | **是** | 目标阶段 |

**示例请求：**

```
PATCH http://localhost:5000/api/rooms/{roomId}/phase
Content-Type: application/json

{"phase":"CharacterPicks"}
```

**成功响应 (200)：**

```json
{
  "roomId": "...",
  "currentPhase": "CharacterPicks",
  ...
}
```

---

### 2.10 更新队伍信息

`PATCH /api/rooms/{roomId}/teams`

**请求体：**

```json
{
  "teamA": {
    "name": "Team Alpha",
    "logoData": null,
    "members": [
      { "id": "p1", "name": "Player 1" },
      { "id": "p2", "name": "Player 2" }
    ]
  },
  "teamB": {
    "name": "Team Beta",
    "logoData": null,
    "members": []
  }
}
```

---

### 2.11 获取事件类型列表

`GET /api/signalr/events`

**示例请求：**

```
GET http://localhost:5000/api/signalr/events
```

**成功响应 (200)：**

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

---

## 三、资源 API

### 3.1 获取所有角色

`GET /api/resources/characters`

**示例请求：**

```
GET http://localhost:5000/api/resources/characters
```

**成功响应 (200)：**

```json
[
  {
    "id": "axe-boy",
    "role": "hunter",
    "imageFileName": "爱哭鬼.png",
    "abbrev": null,
    "fullSpell": null,
    "names": {
      "zh-CN": "爱哭鬼",
      "en-US": "Axe Boy"
    },
    "images": [
      {
        "variant": "full",
        "fileName": "爱哭鬼.png",
        "relativePath": "hunBig/爱哭鬼.png",
        "url": "/resources/hunBig/%E7%88%B1%E5%93%AD%E9%AC%BC.png",
        "contentType": "image/png",
        "extension": ".png",
        "sizeBytes": 430371,
        "lastModifiedUtc": "2026-04-25T02:17:10.4308351+00:00",
        "isPrimary": true
      }
    ]
  }
]
```

---

### 3.2 获取单个角色

`GET /api/resources/characters/{characterId}`

**示例请求：**

```
GET http://localhost:5000/api/resources/characters/mercenary
```

**成功响应 (200)：**

```json
{
  "id": "mercenary",
  "role": "survivor",
  "imageFileName": "佣兵.png",
  "abbrev": null,
  "fullSpell": null,
  "names": {
    "zh-CN": "佣兵",
    "en-US": "Mercenary"
  },
  "images": [
    {
      "variant": "full",
      "fileName": "佣兵.png",
      "relativePath": "surBig/佣兵.png",
      "url": "/resources/surBig/%E4%BD%A3%E5%85%B5.png",
      "contentType": "image/png",
      "extension": ".png",
      "sizeBytes": 260053,
      "lastModifiedUtc": "2026-04-25T02:17:10.5369493+00:00",
      "isPrimary": true
    },
    {
      "variant": "half",
      "fileName": "佣兵.png",
      "relativePath": "surHalf/佣兵.png",
      "url": "/resources/surHalf/%E4%BD%A3%E5%85%B5.png",
      "contentType": "image/png",
      "extension": ".png",
      "sizeBytes": 383525,
      "lastModifiedUtc": "2026-04-25T02:17:10.6020369+00:00",
      "isPrimary": true
    },
    {
      "variant": "header",
      "fileName": "佣兵.png",
      "relativePath": "surHeader/佣兵.png",
      "url": "/resources/surHeader/%E4%BD%A3%E5%85%B5.png",
      "contentType": "image/png",
      "extension": ".png",
      "sizeBytes": 26838,
      "lastModifiedUtc": "2026-04-25T02:17:10.6662508+00:00",
      "isPrimary": true
    },
    {
      "variant": "single-color",
      "fileName": "佣兵.png",
      "relativePath": "surHeader_singleColor/佣兵.png",
      "url": "/resources/surHeader_singleColor/%E4%BD%A3%E5%85%B5.png",
      "contentType": "image/png",
      "extension": ".png",
      "sizeBytes": 20860,
      "lastModifiedUtc": "2026-04-25T02:17:10.6896438+00:00",
      "isPrimary": true
    }
  ]
}
```

**错误响应 (404)：**

```json
{ "message": "Character 'notfound' not found." }
```

---

### 3.3 获取角色图片

`GET /api/resources/characters/{characterId}/images?variant=...`

**查询参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| `variant` | string (可多个) | 图片变体过滤器 |

**支持的角色变体：**

| 变体 | 别名 | 说明 |
|------|------|------|
| `full` | `full-body`, `fullbody` | 全身像 |
| `half` | `half-body`, `halfbody` | 半身像 |
| `header` | - | 头像 |
| `single-color` | `gray`, `grey`, `singlecolor` | 单色剪影 |

**示例请求：**

```
GET http://localhost:5000/api/resources/characters/mercenary/images?variant=full&variant=single-color
```

**成功响应 (200) - 过滤后：**

```json
[
  {
    "variant": "full",
    "fileName": "佣兵.png",
    "relativePath": "surBig/佣兵.png",
    "url": "/resources/surBig/%E4%BD%A3%E5%85%B5.png",
    "contentType": "image/png",
    "extension": ".png",
    "sizeBytes": 260053,
    "lastModifiedUtc": "2026-04-25T02:17:10.5369493+00:00",
    "isPrimary": true
  },
  {
    "variant": "single-color",
    "fileName": "佣兵.png",
    "relativePath": "surHeader_singleColor/佣兵.png",
    "url": "/resources/surHeader_singleColor/%E4%BD%A3%E5%85%B5.png",
    "contentType": "image/png",
    "extension": ".png",
    "sizeBytes": 20860,
    "lastModifiedUtc": "2026-04-25T02:17:10.6896438+00:00",
    "isPrimary": true
  }
]
```

---

### 3.4 获取所有地图

`GET /api/resources/maps`

**示例请求：**

```
GET http://localhost:5000/api/resources/maps
```

**成功响应 (200)：**

```json
[
  {
    "id": "arms-factory",
    "assetKey": "ArmsFactory",
    "names": {
      "zh-CN": "军工厂",
      "en-US": "Arms Factory",
      "ja-JP": "軍需工場"
    },
    "images": [
      {
        "variant": "default",
        "fileName": "ArmsFactory.png",
        "relativePath": "map/ArmsFactory.png",
        "url": "/resources/map/ArmsFactory.png",
        "contentType": "image/png",
        "extension": ".png",
        "sizeBytes": 124214,
        "lastModifiedUtc": "2026-04-25T02:17:10.513199+00:00",
        "isPrimary": true
      }
    ]
  }
]
```

---

### 3.5 获取单个地图

`GET /api/resources/maps/{mapId}`

**示例请求：**

```
GET http://localhost:5000/api/resources/maps/arms-factory
```

**成功响应 (200)：**

```json
{
  "id": "arms-factory",
  "assetKey": "ArmsFactory",
  "names": {
    "zh-CN": "军工厂",
    "en-US": "Arms Factory",
    "ja-JP": "軍需工場"
  },
  "images": [
    {
      "variant": "default",
      "fileName": "ArmsFactory.png",
      "relativePath": "map/ArmsFactory.png",
      "url": "/resources/map/ArmsFactory.png",
      "contentType": "image/png",
      "extension": ".png",
      "sizeBytes": 124214,
      "lastModifiedUtc": "2026-04-25T02:17:10.513199+00:00",
      "isPrimary": true
    },
    {
      "variant": "square",
      "fileName": "ArmsFactory.png",
      "relativePath": "map_square/ArmsFactory.png",
      "url": "/resources/map_square/ArmsFactory.png",
      "contentType": "image/png",
      "extension": ".png",
      "sizeBytes": 122865,
      "lastModifiedUtc": "2026-04-25T02:17:10.5298791+00:00",
      "isPrimary": true
    },
    {
      "variant": "single-color",
      "fileName": "ArmsFactory.png",
      "relativePath": "map_singleColor/ArmsFactory.png",
      "url": "/resources/map_singleColor/ArmsFactory.png",
      "contentType": "image/png",
      "extension": ".png",
      "sizeBytes": 74995,
      "lastModifiedUtc": "2026-04-25T02:17:10.5253574+00:00",
      "isPrimary": true
    },
    {
      "variant": "raw",
      "fileName": "ArmsFactory.webp",
      "relativePath": "map_raw/ArmsFactory.webp",
      "url": "/resources/map_raw/ArmsFactory.webp",
      "contentType": "image/webp",
      "extension": ".webp",
      "sizeBytes": 23174,
      "lastModifiedUtc": "2026-04-25T02:17:10.5192985+00:00",
      "isPrimary": true
    }
  ]
}
```

**错误响应 (404)：**

```json
{ "message": "Map 'notfound' not found." }
```

---

### 3.6 获取地图图片

`GET /api/resources/maps/{mapId}/images?variant=...`

**查询参数：** `variant`（可多个）

**支持的地图变体：**

| 变体 | 说明 |
|------|------|
| `default` | 默认地图 |
| `square` | 方形裁切 |
| `single-color` | 单色剪影 |
| `raw` | 原始素材 |

**示例请求：**

```
GET http://localhost:5000/api/resources/maps/arms-factory/images?variant=square
```

**成功响应 (200)：**

```json
[
  {
    "variant": "square",
    "fileName": "ArmsFactory.png",
    "relativePath": "map_square/ArmsFactory.png",
    "url": "/resources/map_square/ArmsFactory.png",
    "contentType": "image/png",
    "extension": ".png",
    "sizeBytes": 122865,
    "lastModifiedUtc": "2026-04-25T02:17:10.5298791+00:00",
    "isPrimary": true
  }
]
```

---

### 3.7 资源静态直连

服务端将 `Resources` 目录映射为静态路径 `/resources/*`，图片可直接通过 URL 访问：

```
/resources/surBig/%E4%BD%A3%E5%85%B5.png
/resources/map_square/ArmsFactory.png
/resources/hunHeader_singleColor/%E7%88%B1%E5%93%AD%E9%AC%BC.png
```

---

## 四、枚举定义

### BpPhase（BP 阶段）

| 值 | 说明 |
|----|------|
| `Waiting` | 等待开始 |
| `GlobalBans` | 全局禁用阶段 |
| `MapBan` | 地图禁用阶段 |
| `MapPick` | 地图选择阶段 |
| `SideBans` | 阵营禁用阶段 |
| `CharacterPicks` | 角色选择阶段 |
| `Ready` | 就绪 |
| `InProgress` | 比赛中 |
| `Finished` | 已结束 |

### CharacterRole（角色阵营）

| 值 | 说明 |
|----|------|
| `Survivor` | 求生者 |
| `Hunter` | 监管者 |

### GameSide（游戏阵营）

| 值 | 说明 |
|----|------|
| `Survivor` | 求生者方 |
| `Hunter` | 监管者方 |

---

## 五、SignalR 实时通信

### 连接信息

| 项目 | 值 |
|------|-----|
| Hub 地址 | `http://localhost:5000/hubs/game` |
| 推送方法 | `RoomEvent` |

### Hub 方法

| 方法 | 参数 | 说明 |
|------|------|------|
| `JoinRoom` | `roomId: string` | 加入房间组 |
| `LeaveRoom` | `roomId: string` | 离开房间组并移除事件订阅 |
| `ReplaceSubscriptions` | `roomId, eventTypes[]` | 覆盖式设置订阅事件 |
| `SubscribeToEvents` | `roomId, eventTypes[]` | 增量订阅 |
| `UnsubscribeFromEvents` | `roomId, eventTypes[]` | 取消部分订阅 |
| `GetAvailableEventTypes` | 无 | 获取支持的事件类型 |
| `RequestRoomSnapshot` | `roomId: string` | 主动拉取房间完整快照 |

### 推送事件结构

```json
{
  "eventType": "room.map.updated",
  "roomId": "room-001",
  "occurredAtUtc": "2026-04-24T09:30:00+00:00",
  "payload": {}
}
```

### 事件 Payload 对照

| 事件 | Payload 类型 |
|------|-------------|
| `room.snapshot` | 完整 `BpRoom` |
| `room.info.updated` | 完整 `BpRoom` |
| `match.created` | 完整 `BpRoom` |
| `room.map.updated` | `{ currentRound, mapSelection }` |
| `room.ban.updated` | `{ currentRound, role, bans }` |
| `room.global-ban.updated` | `{ role, globalBans }` |
| `room.role.selected` | `{ slot, player, characterPicks }` |
| `room.phase.updated` | `{ phase }` |

### 推荐接入流程

1. 建立 SignalR 连接到 `/hubs/game`
2. 调用 `JoinRoom(roomId)` 加入房间组
3. 调用 `ReplaceSubscriptions(roomId, [...需要的事件])` 订阅事件
4. 调用 `RequestRoomSnapshot(roomId)` 获取初始全量状态
5. 监听 `RoomEvent` 推送处理增量更新
6. 离开页面时调用 `LeaveRoom(roomId)`

---

## 六、C# 客户端 SDK

项目提供完整的客户端封装：

### BpApiClient（REST）

```csharp
// 房间操作
GetRoomsAsync()              // GET /api/rooms
GetRoomAsync(roomId)         // GET /api/rooms/{id}
CreateRoomAsync(request)     // POST /api/rooms
CreateMatchAsync(id, req)    // POST /api/rooms/{id}/matches
AddBanAsync(id, req)         // POST /api/rooms/{id}/bans
AddGlobalBanAsync(id, req)   // POST /api/rooms/{id}/global-bans
SelectRoleAsync(id, req)     // POST /api/rooms/{id}/roles
UpdateMapAsync(id, req)      // PATCH /api/rooms/{id}/map
UpdatePhaseAsync(id, req)    // PATCH /api/rooms/{id}/phase
UpdateTeamsAsync(id, req)    // PATCH /api/rooms/{id}/teams

// 资源操作
GetCharactersAsync()         // GET /api/resources/characters
GetCharacterAsync(id)        // GET /api/resources/characters/{id}
GetCharacterImagesAsync(id, variants)  // GET /api/resources/characters/{id}/images
GetMapsAsync()               // GET /api/resources/maps
GetMapAsync(id)              // GET /api/resources/maps/{id}
GetMapImagesAsync(id, variants)       // GET /api/resources/maps/{id}/images
```

### SignalRClient（实时通信）

```csharp
// 连接管理
StartAsync()
StopAsync()

// 房间组
JoinRoomAsync(roomId)
LeaveRoomAsync(roomId)

// 事件订阅
ReplaceSubscriptionsAsync(roomId, eventTypes)
SubscribeToEventsAsync(roomId, eventTypes)
UnsubscribeFromEventsAsync(roomId, eventTypes)
RequestRoomSnapshotAsync(roomId)
GetAvailableEventTypesAsync()

// 事件监听
OnRoomEvent(Action<RoomEventEnvelope> handler)
```

### RoomRealtimeClient（高级封装）

```csharp
EnsureConnectedAsync()                        // 确保连接已建立
SubscribeToRoomAsync(roomId, eventTypes)       // 加入房间并订阅事件
LeaveRoomAsync(roomId)                         // 离开房间
RequestRoomSnapshotAsync(roomId)               // 请求房间快照
```
