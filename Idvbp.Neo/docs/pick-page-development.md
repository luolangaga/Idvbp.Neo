# 角色选择页开发文档

## 功能范围

角色选择页用于完成当前比赛房间的角色选择操作。

当前实现包含：

- 主窗口统一管理比赛房间
- 选角页读取当前房间状态
- 通过资源 API 加载所有角色
- 按求生者 / 监管者分类展示角色下拉框
- 选择角色后提交到内置服务器
- 通过 SignalR 接收房间状态更新
- 加载本地半身图作为角色预览

## 关键代码位置

- `Views/Pages/PickPage.axaml`
  - 选角页 UI。
  - 只负责角色选择、图片预览、选择框控制、Ban 记录展示。
  - 不再包含房间选择和房间状态展示，房间管理统一在主窗口顶部。

- `ViewModels/Pages/PickPageViewModel.cs`
  - 选角页状态和交互逻辑。
  - 加载角色资源。
  - 维护求生者 4 个槽位和监管者 1 个槽位。
  - 选择角色后提交选角。
  - 接收共享房间工作区的当前房间。
  - 加载本地半身图预览。

- `Service/BpRoomWorkspace.cs`
  - 全局房间工作区。
  - 主窗口和选角页共享同一个 `SelectedRoom`。
  - 管理房间列表、当前房间、SignalR 订阅、房间快照。

- `Client/BpApiClient.cs`
  - 桌面端 HTTP API 客户端。
  - 负责调用内置服务器 REST API。

- `Client/RoomRealtimeClient.cs`
  - 桌面端 SignalR 客户端封装。
  - 负责连接 `/hubs/game`、订阅房间事件、请求房间快照。

- `Views/MainWindow.axaml`
  - 主窗口顶部的比赛房间管理入口。
  - 新建比赛、刷新房间、选择当前房间、新开一局都在这里操作。

- `ViewModels/MainWindowViewModel.cs`
  - 主窗口状态和命令。
  - 调用 `BpRoomWorkspace` 管理比赛房间。

## 连接架构

当前采用“HTTP 写入 + SignalR 同步”的架构。

HTTP 负责：

- 获取角色资源
- 获取房间列表
- 新建比赛房间
- 新开一局
- 提交选角

SignalR 负责：

- 加入当前房间
- 订阅当前房间事件
- 请求房间快照
- 接收房间状态变化

## 内置服务器地址

默认配置在 `appsettings.json`：

```json
{
  "Server": {
    "Urls": "http://localhost:5000"
  }
}
```

桌面端通过 `App.Services.cs` 注册客户端：

```csharp
services.AddSingleton(_ => new BpApiClient(context.Configuration["Server:Urls"] ?? "http://localhost:5000"));
services.AddSingleton<RoomRealtimeClient>();
services.AddSingleton<BpRoomWorkspace>();
```

## HTTP API 使用

### 获取角色资源

客户端调用：

```csharp
await _apiClient.GetCharactersAsync();
```

服务端接口：

```http
GET /api/resources/characters
```

返回角色结构来自 `Server/Resources/ResourceModels.cs` 的 `CharacterResourceItem`。

选角页会使用：

- `Id`
- `Role`
- `Names["zh-CN"]`
- `Images` 中 `Variant == "half"` 的半身图

### 获取房间列表

客户端调用：

```csharp
await _apiClient.GetRoomsAsync();
```

服务端接口：

```http
GET /api/rooms
```

这个接口由 `BpRoomWorkspace.RefreshRoomsAsync()` 调用，主窗口和选角页不要各自维护房间列表。

### 新建比赛

客户端调用：

```csharp
await _apiClient.CreateRoomAsync(new CreateRoomRequest
{
    RoomName = "默认比赛",
    TeamAName = "主队",
    TeamBName = "客队",
    MapBanSlotsPerSide = 1,
    SurvivorBanSlots = 2,
    HunterBanSlots = 2,
    GlobalSurvivorBanSlots = 1,
    GlobalHunterBanSlots = 1
});
```

服务端接口：

```http
POST /api/rooms
```

新建比赛成功后，`BpRoomWorkspace` 会把返回的 `BpRoom` 放入房间列表并设置为当前房间。

### 新开一局

客户端调用：

```csharp
await _apiClient.CreateMatchAsync(roomId, new CreateMatchRequest
{
    CurrentPhase = BpPhase.GlobalBans,
    ResetGlobalBans = false
});
```

服务端接口：

```http
POST /api/rooms/{roomId}/matches
```

该接口会推进 `CurrentRound`，重置当前局地图、Ban、选角状态，并按需重置全局 Ban。

### 提交选角

客户端调用：

```csharp
await _apiClient.SelectRoleAsync(roomId, new SelectRoleRequest
{
    Slot = "Survivor1",
    PlayerId = "team-a-survivor1",
    PlayerName = "求生者 1",
    TeamId = "team-a",
    CharacterId = "priestess"
});
```

服务端接口：

```http
POST /api/rooms/{roomId}/roles
```

`Slot` 支持：

- `Survivor1`
- `Survivor2`
- `Survivor3`
- `Survivor4`
- `Hunter`

提交成功后服务端会发布 `room.role.selected` SignalR 事件。

## SignalR 使用

SignalR Hub 地址：

```text
/hubs/game
```

桌面端封装在 `RoomRealtimeClient`：

```csharp
await _realtimeClient.SubscribeToRoomAsync(roomId, eventTypes);
await _realtimeClient.RequestRoomSnapshotAsync(roomId);
```

订阅事件列表在 `BpRoomWorkspace` 中维护：

```csharp
private static readonly string[] RealtimeEventTypes =
[
    RoomEventNames.RoomSnapshot,
    RoomEventNames.RoomInfoUpdated,
    RoomEventNames.MatchCreated,
    RoomEventNames.RoleSelected,
    RoomEventNames.BanUpdated,
    RoomEventNames.GlobalBanUpdated,
    RoomEventNames.PhaseUpdated
];
```

收到事件后由 `BpRoomWorkspace.ApplyRoomEvent` 折叠成最新房间状态。

选角页不要直接自己订阅房间事件，优先使用 `BpRoomWorkspace.SelectedRoom`。

## 状态流

### 初始化

1. 程序启动内置 ASP.NET Core 服务。
2. 主窗口创建 `MainWindowViewModel`。
3. `MainWindowViewModel` 调用 `Workspace.RefreshRoomsAsync()`。
4. `BpRoomWorkspace` 拉取房间列表并选中最近更新的房间。
5. `BpRoomWorkspace` 通过 SignalR 订阅当前房间并请求 `room.snapshot`。
6. 选角页初始化时加载角色资源，并从 `BpRoomWorkspace.SelectedRoom` 回填槽位。

### 切换房间

1. 用户在主窗口顶部选择房间。
2. `Workspace.SelectedRoom` 改变。
3. `BpRoomWorkspace` 订阅新房间 SignalR 事件。
4. 请求新房间快照。
5. 选角页收到 `Workspace.SelectedRoom` 变化并刷新槽位。

### 选择角色

1. 用户在选角页下拉框选择角色。
2. `PickSlotItem.SelectedCharacter` 变化。
3. 选角页立即加载半身图预览。
4. 选角页短防抖后调用 `POST /api/rooms/{roomId}/roles`。
5. 服务端持久化选角并发布 `room.role.selected`。
6. `BpRoomWorkspace` 接收 SignalR 事件并更新当前房间。
7. 选角页原地更新槽位，不重建整个页面。

## 图片加载

角色图片优先使用本地资源，不依赖 HTTP 静态资源路径。

半身图路径来自资源 API 返回的 `Images[].RelativePath`。

示例：

```text
surHalf/咒术师.png
hunHalf/歌剧演员.png
```

加载逻辑在 `PickPageViewModel.LoadSlotPreviewImageAsync`：

1. 先查内存缓存 `_previewImageCache`
2. 再尝试读取本地文件：
   - `AppContext.BaseDirectory/Resources/{relativePath}`
   - `Directory.GetCurrentDirectory()/Resources/{relativePath}`
   - `Directory.GetCurrentDirectory()/Idvbp.Neo/Resources/{relativePath}`
3. 本地读取失败才使用 HTTP URL 兜底

维护注意：

- 新增角色时必须保证 `Resources/data/CharacterList.json` 里的 `ImageFileName` 和图片文件名一致。
- 求生者半身图放在 `Resources/surHalf`。
- 监管者半身图放在 `Resources/hunHalf`。
- 图片文件会复制到输出目录，因为项目文件中有：

```xml
<Content Include="Resources\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

## 页面维护规则

### 不要在选角页管理房间

房间选择、新建比赛、新开一局统一在主窗口顶部。

选角页只消费：

```csharp
BpRoomWorkspace.SelectedRoom
```

不要在 `PickPage.axaml` 重新加房间下拉框，否则会出现多个页面状态不一致。

### 不要重建槽位集合

选择角色后页面不能闪烁。

因此不要在每次 `ApplyRoom` 时重新赋值：

```csharp
SurPickList = new ObservableCollection<PickSlotItem>(...);
HunPickVm = new PickSlotItem();
```

正确做法是：

- 首次创建 4 个求生者槽位
- 后续通过 `UpdateSlotFromRoom` 原地更新每个槽位属性

### 不要直接把 URL 字符串绑定给 Image.Source

Avalonia 下直接绑定 URL 字符串可能因为解码、静态资源、流释放等原因显示空白。

当前做法是：

```csharp
Bitmap bitmap = new Bitmap(memoryStream);
slot.SelectedCharacterPreviewImage = bitmap;
```

XAML 中绑定：

```xml
<Image Source="{Binding SelectedCharacterPreviewImage}" />
```

### SignalR 是状态同步主链路

不要用页面各自轮询房间详情。

写操作可以使用 HTTP，写完后应等待 SignalR 事件把最终状态同步回来。

## 常见问题

### 页面选择后闪一下

原因通常是重新创建了 `ObservableCollection` 或整个槽位 ViewModel。

检查 `ApplyRoom` 是否仍然在重建 `SurPickList` 或 `HunPickVm`。

### 图片空白

检查：

- `CharacterList.json` 的 `ImageFileName` 是否正确
- `Resources/surHalf` 或 `Resources/hunHalf` 是否存在对应图片
- 输出目录 `bin/Debug/net10.0/Resources/...` 是否复制了图片
- `PickPageViewModel.TryLoadLocalPreviewImage` 的候选路径是否覆盖当前运行目录

### 启动时报 No precompiled XAML found

这是 Avalonia 的 XAML 预编译产物问题，通常由脏的 `obj/bin` 或混用 `dotnet build -o` 输出导致。

处理方式：

```powershell
dotnet clean Idvbp.Neo/Idvbp.Neo.csproj
dotnet build Idvbp.Neo/Idvbp.Neo.csproj
```

不要在同一个项目反复混用默认输出和 `dotnet build -o <custom>`，否则可能污染 Avalonia 的中间资源缓存。

### 选角页没有房间

先在主窗口顶部新建比赛或刷新房间。

选角页不会自己创建房间。

## 扩展建议

### 接入选手名单

当前槽位默认生成 `PlayerId`、`PlayerName`、`TeamId`。

后续如果队伍信息页接入真实名单，可以在 `UpdateSlotFromRoom` 中优先使用当前上场选手信息。

### 选角写操作改成 SignalR

当前服务端 Hub 只提供订阅、快照和事件推送，写操作还是 REST。

如果需要“选角提交也走 SignalR”，需要在 `GameHub` 增加类似：

```csharp
public Task<BpRoom> SelectRole(string roomId, SelectRoleRequest request)
```

然后内部调用 `IRoomService.SelectRoleAsync`。

### 多页面共享更多状态

如果后续地图 BP、Ban 页面也需要实时同步，应优先把状态放到 `BpRoomWorkspace`，页面只做展示和命令调用。
