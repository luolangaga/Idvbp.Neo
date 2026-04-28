# 选角页面实现说明

## 概述

`PickPage` 已迁移到当前 Avalonia 程序，并接通了内嵌服务端暴露的 REST API。

主窗口现在通过 `BpRoomWorkspace` 管理当前比赛房间，选角页复用同一个当前房间状态，避免页面之间各自维护房间导致状态分叉。

当前页面支持：

- 读取 `/api/resources/characters` 获取全部角色资源并按阵营分类缓存
- 读取 `/api/rooms` 获取房间列表
- 通过 `SignalR` 订阅房间事件并请求 `room.snapshot`
- 主窗口支持新建比赛、刷新房间、选择房间、新开一局
- 在求生者 4 个槽位和监管者 1 个槽位上直接选择角色
- 角色变更后调用 `/api/rooms/{roomId}/roles` 提交，随后以 `SignalR` 实时事件驱动页面状态同步
- 展示当前房间的全局 Ban / 当前局 Ban 记录

## 页面结构

页面由四块组成：

- 房间与状态区：选择房间、查看当前阶段与回合、手动刷新
- 求生者选角区：4 个槽位，每个槽位可维护选手名、选手 ID、队伍 ID、角色选择
- 监管者选角区：1 个槽位，逻辑与求生者一致
- 配置与记录区：保留原页面的选择框配置入口，并展示后端已有的 Ban 数据

## API 对接方式

桌面端新增 `Client/BpApiClient.cs`，统一封装以下接口：

- `GET /api/resources/characters`
- `GET /api/rooms`
- `POST /api/rooms/{roomId}/roles`

桌面端同时新增 `Client/RoomRealtimeClient.cs`，负责：

- 连接 `/hubs/game`
- `JoinRoom(roomId)`
- `ReplaceSubscriptions(roomId, eventTypes)`
- `RequestRoomSnapshot(roomId)`
- 接收统一的 `RoomEvent` 事件信封

桌面端新增 `Service/BpRoomWorkspace.cs`，作为全局房间工作区：

- 保存房间列表与当前房间
- 主窗口、选角页共享同一个 `SelectedRoom`
- 负责房间级 SignalR 订阅与快照请求
- 把 `room.snapshot`、`room.role.selected`、`match.created` 等事件折叠成最新 `BpRoom`

这样做有两个目的：

- 页面不直接依赖底层 `HttpClient`，后续扩展到 Ban、地图、阶段控制时可以直接复用
- 资源 URL 会在客户端统一补成绝对地址，避免页面层重复拼接和重复判断

## 选角提交流程

1. 页面初始化时拉取角色目录，并建立 `id -> 角色` 的只读索引
2. 主窗口通过 `BpRoomWorkspace` 拉取房间列表，并默认选中最近更新的房间
3. 选中房间后由 `BpRoomWorkspace` 通过 `SignalR` 加入房间、替换订阅事件、请求房间快照
4. 收到 `room.snapshot` 后，根据当前 `TeamA.CurrentSide / TeamB.CurrentSide` 推导当前求生者方和监管者方
5. 用当前房间里的 `CharacterPicks` 回填 5 个槽位
6. 用户变更角色后，页面做一次短暂防抖，再调用选角 API
7. 后续房间状态以 `SignalR` 事件为准，页面通过 `room.role.selected` 等事件实时刷新

## 性能与扩展性处理

- 角色资源目录只加载一次，避免每次切换房间重复请求
- 求生者/监管者角色列表复用同一份只读集合，避免为每个槽位重复构造数据
- 角色选择提交加入了短防抖，减少频繁切换下的无效请求
- 页面状态更新统一走 `ApplyRoom`，HTTP 初始化与 SignalR 实时事件都复用同一套状态折叠逻辑
- API 调用集中在 `BpApiClient`，便于后续抽出接口、补测试或切换到远程服务

## 当前约束

- 原始 WPF 页面里的“主队/客队分别记录全局 Ban”在现有后端模型中尚未完全对应；当前页面按“房间级全局 Ban + 当前局 Ban”稳定展示
- 选择框开关目前保留为本地页面状态，用于兼容原始布局和后续前端展示层扩展，暂未写回后端

## 后续建议

1. 如果要支持多端同步，下一步可以在 `PickPageViewModel` 上接入 `SignalRClient`，收到 `room.role.selected` 或 `room.snapshot` 后直接复用 `ApplyRoom`
2. 如果后端后续补充队伍完整名单和角色位映射，页面可以把槽位中的 `PlayerId/PlayerName/TeamId` 从手填切换为只读自动带出
