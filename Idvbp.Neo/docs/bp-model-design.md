# BP 模型设计文档

## 核心关系一览

```
┌─────────────────────────────────────────────────────────┐
│                        BpRoom                           │
│  (聚合根 - 一次完整 BP 对局的全部状态)                      │
├─────────────────────────────────────────────────────────┤
│  RoomId, RoomName                                       │
│  CurrentPhase :BpPhase          ← 阶段状态机              │
│  CurrentRound :int              ← 当前第几回合             │
│                                                            │
│  TeamA :Team                    ← 队伍 A                 │
│    ├── Id, Name, LogoUrl                                 │
│    ├── CurrentSide :GameSide    ← 每轮轮换 (Survivor↔Hunter)│
│    └── Members :List<Player>    ← 队员列表                 │
│  TeamB :Team                    ← 队伍 B                 │
│    ├── ... (同 TeamA)                                    │
│    └── CurrentSide 始终与 TeamA 相反                       │
│                                                            │
│  MapSelection :MapSelection      ← 地图 Ban/Pick          │
│    ├── BannedMaps :List<MapBanEntry> (双方 Ban 的地图)     │
│    │     ├── MapId, Order                                │
│    │     └── ... (可 Ban 多张)                             │
│    ├── PickedMap  :MapInfo?       ← 最终选用的地图          │
│    └── BanSlotsPerSide :int       ← 每方可 Ban 地图数       │
│                                                            │
│  CharacterPicks :CharacterPickSelection                   │
│    ├── Survivor1 :Player ──→ CharacterInfo (选了什么角色)    │
│    ├── Survivor2 :Player ──→ CharacterInfo                │
│    ├── Survivor3 :Player ──→ CharacterInfo                │
│    ├── Survivor4 :Player ──→ CharacterInfo                │
│    └── Hunter    :Player ──→ CharacterInfo                │
│                                                            │
│  Bans :BanSelection                                       │
│    ├── SurvivorBans :List<PickBanEntry>   (求生者被Ban角色)  │
│    └── HunterBans   :List<PickBanEntry>   (监管者被Ban角色)  │
│                                                            │
│  GlobalBans :GlobalBanSelection                           │
│    ├── SurvivorBans :List<PickBanEntry>   (全局求生者Ban)    │
│    └── HunterBans   :List<PickBanEntry>   (全局监管者Ban)    │
│                                                            │
│  MatchScore :MatchScore                                   │
│    └── Rounds :List<RoundScore>                           │
│          ├── Round 1                                      │
│          │    ├── FirstHalf  :RoundHalfScore (上半局)       │
│          │    └── SecondHalf :RoundHalfScore (下半局)       │
│          ├── Round 2                                      │
│          │    ├── FirstHalf  :RoundHalfScore              │
│          │    └── SecondHalf :RoundHalfScore              │
│          └── ... (BO3/BO5 任意局数)                         │
└─────────────────────────────────────────────────────────┘
```

## 关键关系说明

| 关系 | 方向 | 说明 |
|---|---|---|
| **Player → Team** | N:1 归属 | Player.TeamId 指向所属队伍 |
| **Player → Character** | Player 包含 CharacterId | Player.CharacterId 指向该选手选择的角色 |
| **BpRoom → Team** | 1:2 | 一个房间有两支队伍，每轮双方阵营轮换 |
| **Team → Player** | 1:N | 一个队伍包含多名队员 |
| **BpRoom → CharacterPickSelection** | 1:1 聚合 | 一个房间有一组选角结果 |
| **CharacterPickSelection → Player** | 1:5 组合 | 4 求生者选手 + 1 监管者选手 |
| **BpRoom → MapSelection** | 1:1 聚合 | 地图 Ban/Pick 结果 |
| **MapSelection → MapBanEntry** | 1:N | 双方 Ban 掉的地图列表 |
| **BanSelection → PickBanEntry** | 1:N | 每个阵营的 Ban 列表 |
| **GlobalBanSelection → PickBanEntry** | 1:N | 全局 Ban 列表 |
| **MatchScore → RoundScore** | 1:N | N 个回合 (BO3/BO5) |
| **RoundScore → RoundHalfScore** | 1:2 | 上半局 + 下半局 |

---

## 枚举定义

### BpPhase — BP 阶段状态机

```
Waiting ──→ GlobalBans ──→ MapBan ──→ MapPick ──→ SideBans ──→ CharacterPicks ──→ Ready ──→ InProgress ──→ Finished
```

| 值 | 含义 |
|---|---|
| `Waiting` | 等待开始 |
| `GlobalBans` | 全局 Ban（双方各 Ban 若干角色，全场生效） |
| `MapBan` | 地图 Ban（双方各 Ban 多张地图） |
| `MapPick` | 地图选择（从剩余地图中选出本局地图） |
| `SideBans` | 阵营 Ban（当前回合的额外 Ban） |
| `CharacterPicks` | 选手选角 |
| `Ready` | BP 完成，等待开局 |
| `InProgress` | 对局进行中 |
| `Finished` | 对局结束 |

### GameSide

| 值 | 含义 |
|---|---|
| `Survivor` | 求生者阵营 |
| `Hunter` | 监管者阵营 |

### CharacterRole

| 值 | 含义 |
|---|---|
| `Survivor` | 求生者角色 |
| `Hunter` | 监管者角色 |

---

## 模型详解

### Player — 选手

选手归属于队伍，在每轮比赛中根据队伍当前阵营担任求生者或监管者。

| 属性 | 类型 | 说明 |
|---|---|---|
| `Id` | `string` | 选手 ID |
| `Name` | `string` | 选手名称 |
| `AvatarUrl` | `string?` | 选手头像地址 |
| `TeamId` | `string` | 所属队伍 ID，指向 Team |
| `SeatNumber` | `int` | 席位号（求生者 1~4，监管者 1） |
| `CharacterId` | `string` | 该选手选择的角色 ID，指向 CharacterInfo |

> 注意：选手不再持有 `GameSide`，阵营由所属队伍在当前回合的 `Team.CurrentSide` 决定（每轮求生者/监管者轮换）。

### Team — 队伍

| 属性 | 类型 | 说明 |
|---|---|---|
| `Id` | `string` | 队伍 ID |
| `Name` | `string` | 队伍名称 |
| `LogoUrl` | `string?` | 队伍 Logo 地址 |
| `Members` | `ObservableCollection<Player>` | 队员列表 |
| `CurrentSide` | `GameSide` | 当前回合的阵营，每轮在 Survivor/Hunter 间轮换 |

> BpRoom 中 `TeamA` 和 `TeamB` 的 `CurrentSide` 始终相反。调用 `BpRoom.StartNewRound()` 时自动交换双方阵营并重置当轮 BP 数据。首轮默认 TeamA 为 Survivor、TeamB 为 Hunter。

### MapSelection — 地图 Ban/Pick

| 属性 | 类型 | 说明 |
|---|---|---|
| `BannedMaps` | `ObservableCollection<MapBanEntry>` | 双方 Ban 掉的地图列表 |
| `PickedMap` | `MapInfo?` | 最终选用的地图 |
| `BanSlotsPerSide` | `int` | 每方可 Ban 地图数量（可配置） |

### MapBanEntry — 地图 Ban 条目

| 属性 | 类型 | 说明 |
|---|---|---|
| `MapId` | `string` | 被 Ban 的地图 ID |
| `Order` | `int` | Ban 的顺序（从 1 开始） |

### CharacterInfo — 角色

纯数据记录，不可变。

| 属性 | 类型 | 说明 |
|---|---|---|
| `Id` | `string` | 角色唯一标识 |
| `Name` | `string` | 角色名称 |
| `AvatarUrl` | `string?` | 头像地址 |
| `Role` | `CharacterRole` | 求生者角色 / 监管者角色 |

### MapInfo — 地图

纯数据记录，不可变。

| 属性 | 类型 | 说明 |
|---|---|---|
| `Id` | `string` | 地图唯一标识 |
| `Name` | `string` | 地图名称 |
| `ImageUrl` | `string?` | 地图图片地址 |

### PickBanEntry — Ban 条目

一次 Ban 操作的记录。

| 属性 | 类型 | 说明 |
|---|---|---|
| `CharacterId` | `string` | 被 Ban 的角色 ID |
| `Order` | `int` | Ban 的顺序（从 1 开始） |

### CharacterPickSelection — 选角结果

包含 5 个选手及其选择的角色。

```
Survivor1 :Player  ← 求生者 1 号位选手
Survivor2 :Player  ← 求生者 2 号位选手
Survivor3 :Player  ← 求生者 3 号位选手
Survivor4 :Player  ← 求生者 4 号位选手
Hunter    :Player  ← 监管者选手
```

每个 `Player` 的 `CharacterId` 就是该选手选择的角色。

### BanSelection — 阵营 Ban

| 属性 | 类型 | 说明 |
|---|---|---|
| `SurvivorBans` | `ObservableCollection<PickBanEntry>` | 求生者阵营被 Ban 角色列表 |
| `HunterBans` | `ObservableCollection<PickBanEntry>` | 监管者阵营被 Ban 角色列表 |
| `SurvivorBanSlots` | `int` | 求生方可 Ban 数量（可配置） |
| `HunterBanSlots` | `int` | 监管方可 Ban 数量（可配置） |

### GlobalBanSelection — 全局 Ban

结构同 BanSelection，语义不同：**全局 Ban 全场生效，阵营 Ban 仅当前回合生效**。两者分开存储，互不干扰。

### RoundHalfScore — 半局比分

| 属性 | 类型 | 说明 |
|---|---|---|
| `SurvivorScore` | `int` | 求生者半局得分 |
| `HunterScore` | `int` | 监管者半局得分 |

### RoundScore — 单回合

| 属性 | 类型 | 说明 |
|---|---|---|
| `RoundNumber` | `int` | 第几回合（1,2,3...） |
| `FirstHalf` | `RoundHalfScore` | 上半局比分 |
| `SecondHalf` | `RoundHalfScore` | 下半局比分 |
| `SurvivorRoundScore` | `int` | **计算属性**：上下半局求生者合计 |
| `HunterRoundScore` | `int` | **计算属性**：上下半局监管者合计 |
| `RoundWinner` | `GameSide?` | **计算属性**：该回合胜者 (null = 平局) |

### MatchScore — 全场总分

| 属性 | 类型 | 说明 |
|---|---|---|
| `Rounds` | `ObservableCollection<RoundScore>` | 全部回合 |
| `SurvivorMatchScore` | `int` | 求生者胜局总数 |
| `HunterMatchScore` | `int` | 监管者胜局总数 |
| `TotalRounds` | `int` | 已完赛回合数 |
| `MatchWinner` | `GameSide?` | 全场胜者 (null = 未结束/平局) |

`AddRound(round)` 后自动调用 `RecalculateTotals()` 重算总比分。

---

## 设计原则

| 原则 | 实现方式 |
|---|---|
| **队伍阵营轮换** | `Team.CurrentSide` + `BpRoom.StartNewRound()` 自动交换双方阵营 |
| **选手归属队伍** | `Player.TeamId` 指向 Team，阵营由队伍当前轮决定 |
| **地图 Ban/Pick 分离** | `MapSelection` 独立管理 Ban 列表和最终选择，支持每方 Ban 多张地图 |
| **可扩展 Ban 位数** | `BanSlots` 属性可动态配置（BO3 2 ban / BO5 3 ban） |
| **可扩展回合数** | `MatchScore.Rounds` 是动态列表，天然支持 BO1~BO7 |
| **可扩展阶段** | `BpPhase` 枚举可追加新值而不破坏现有逻辑 |
| **MVVM 双向绑定** | 所有实体继承 `ObservableObject` + `[ObservableProperty]` 源生成器 |
| **SignalR 广播就绪** | `BpRoom` 作为聚合根，一次序列化即可全量同步 |
| **选手-角色关联** | `Player.CharacterId` 指向 `CharacterInfo.Id`，选手和自己选的角色的清晰关联 |
