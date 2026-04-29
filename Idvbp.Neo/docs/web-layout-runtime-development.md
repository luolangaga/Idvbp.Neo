# Web 前台布局运行时开发文档

本文档对应“新前台系统完整设计”，用于说明 Idvbp.Neo 里 Web 反代前台页面、布局运行时、前台包、BP 数据展示示例的开发约定。

## 目标

前台不是由 Avalonia 或 ASP.NET Core 直接创建 UI 控件，而是由浏览器端 layout runtime 创建。

核心分工：

| 层级 | 职责 |
| --- | --- |
| ASP.NET Core / Kernel | 提供 BP 数据 API、静态资源、前台包、SignalR 事件 |
| Avalonia 后台 | 操作 BP 流程、切换阶段、调用内核服务 |
| Layout Runtime | 读取 `manifest.json` 和 `layout.json`，加载组件，维护 store，处理 bind 和 events |
| 前台包 | 提供 layout、组件脚本、组件样式、资源 |
| 组件 | 通过 `type` 注册，由 runtime 根据 layout 创建和更新 |

## 访问方式

官方 runtime 位置：

```text
wwwroot/runtime/layout-renderer/index.html
```

示例前台包位置：

```text
wwwroot/frontends/bp-demo/
```

固定打开地址：

```text
/bp-layout?frontend=bp-demo
/bp-layout?frontend=bp-demo&page=main&roomId={roomId}
```

`/bp-layout` 是内核固定入口，不需要在 `proxies.json` 里手动配置。runtime 会根据 `frontend` 和 `page` 参数自动加载：

```text
wwwroot/frontends/{frontend}/manifest.json
wwwroot/frontends/{frontend}/{page.layout}
```

如果没有传 `roomId`，runtime 会调用 `/api/rooms` 并使用第一个房间；如果没有房间，会显示空 BP 示例状态。

## 前台包结构

```text
wwwroot/frontends/bp-demo/
├─ manifest.json
├─ layout.json
└─ components/
   ├─ bp-room-summary.js
   ├─ bp-room-summary.css
   ├─ team-card.js
   ├─ team-card.css
   ├─ bp-selection-list.js
   └─ bp-selection-list.css
```

`manifest.json` 只描述包元数据和组件资源，不写布局细节。

`layout.json` 只描述组件树、位置、props、bind、局部 css 和事件响应。

## manifest.json 协议

```json
{
  "id": "bp-demo",
  "name": "BP Demo Layout",
  "version": "1.0.0",
  "type": "layout-template",
  "entryLayout": "layout.json",
  "pages": [
    {
      "id": "main",
      "name": "BP Main",
      "layout": "layout.json"
    }
  ],
  "components": [
    {
      "type": "team-card",
      "script": "components/team-card.js",
      "style": "components/team-card.css"
    }
  ]
}
```

约定：

| 字段 | 说明 |
| --- | --- |
| `id` | 前台包 ID，对应 URL 的 `frontend` 参数 |
| `entryLayout` | 入口布局文件 |
| `pages[]` | 包内页面列表；没有写时后端会扫描 layout json 自动识别 |
| `components[].type` | 组件类型 ID，必须和组件注册时的 type 一致 |
| `components[].script` | 组件脚本，相对前台包根目录 |
| `components[].style` | 组件样式，相对前台包根目录 |

## layout.json 协议

```json
{
  "schemaVersion": 1,
  "id": "bp-demo-layout",
  "canvas": {
    "width": 1920,
    "height": 1080
  },
  "nodes": [
    {
      "id": "team-a",
      "type": "team-card",
      "props": {
        "teamName": {
          "bind": "room.teamA.name"
        }
      },
      "style": {
        "left": 70,
        "top": 230,
        "width": 760,
        "height": 300,
        "zIndex": 10
      },
      "css": ":host { --team-accent: #57b8ff; }",
      "events": {
        "room.info.updated": [
          {
            "action": "playAnimation",
            "name": "pulse"
          }
        ]
      }
    }
  ]
}
```

节点字段：

| 字段 | 说明 |
| --- | --- |
| `id` | 当前组件实例 ID |
| `type` | 组件类型 ID |
| `props` | 传给组件的参数 |
| `bind` | 在 props 内声明数据绑定路径 |
| `style` | 位置、大小、层级 |
| `css` | scoped 到当前 node 的局部样式 |
| `events` | 当前组件响应内核事件的 action 列表 |
| `children` | 子组件节点 |

## 数据绑定

runtime 维护本地 store：

```ts
type RuntimeStore = {
  room: BpRoom | null;
  event: FrontendEvent | null;
};
```

`bind` 从 store 读取数据：

```json
{
  "teamName": {
    "bind": "room.teamA.name"
  }
}
```

这表示组件的 `teamName` prop 始终来自当前房间状态。BP 数据变更后，runtime 会重新计算所有 bind 并刷新组件。

## 事件和 action

SignalR 收到 `RoomEvent` 后，runtime 会：

1. 读取 envelope 的 `eventType`。
2. 用 `payload` 更新 `store.room`。
3. 刷新所有 bind。
4. 查找每个 node 的 `events[eventType]`。
5. 执行命中的 action。

内置 action：

| action | 参数 | 说明 |
| --- | --- | --- |
| `playAnimation` | `name` | 播放 `enter`、`leave`、`pulse` |
| `stopAnimation` | 无 | 停止内置动画 class |
| `setVisible` | `value` | 显示或隐藏 node |
| `setClass` | `name` | 添加 class |
| `removeClass` | `name` | 移除 class |
| `toggleClass` | `name`, `value` | 切换 class |
| `setProp` | `name`, `value` | 写入 node props 并刷新 |
| `emit` | `type`, `payload` | 触发前台本地事件 |

## 组件注册协议

组件脚本通过全局 runtime SDK 注册：

```js
window.IdvbpLayoutRuntime.register("team-card", {
    render(element, props, context) {
        element.innerHTML = `<section>${props.teamName}</section>`;
    },
    actions: {
        flashScore(element, action, context, event) {
            element.classList.add("flash");
        }
    }
});
```

约定：

| 参数 | 说明 |
| --- | --- |
| `element` | runtime 为当前 node 创建的 DOM 容器 |
| `props` | 已经解析过 bind 的 props |
| `context.node` | 当前 layout node |
| `context.store` | 当前 runtime store |
| `context.frontendBase` | 当前前台包静态路径 |
| `context.emit` | 触发前台本地事件 |

React/Vue 只能作为组件内部实现方式，不能进入 layout 协议。layout 只识别 `type`。

## 前台包管理 API

前台包统一放在：

```text
wwwroot/frontends/{packageId}/
```

后端启动后会自动扫描 `wwwroot/frontends`，读取每个包的 `manifest.json`，并自动识别页面。

桌面端入口在“Web 反向代理”页面：

| 功能 | 说明 |
| --- | --- |
| 导入 ZIP 前台包 | 选择 zip 后自动解包到 `wwwroot/frontends/{manifest.id}` |
| 自动页面列表 | 显示每个包识别到的 `pages` / layout |
| 打开页面 | 直接用内置 WebView 打开 `/bp-layout?...` |
| 复制地址 | 复制默认页或指定页面 URL |
| 编辑布局 | 用内置 WebView 打开 `/bp-layout?...&edit=1`，可拖动组件、缩放组件、修改坐标和尺寸 |
| 保存布局 | 编辑器调用后端 API，把当前 layout JSON 写回前台包内对应 layout 文件 |
| 反代路由 | 保留原有 `proxies.json` 路由列表和页面 config 编辑 |

API：

| API | 说明 |
| --- | --- |
| `GET /api/frontends` | 列出所有前台包和页面 |
| `GET /api/frontends/{id}` | 获取单个前台包信息 |
| `POST /api/frontends/import` | 上传 zip 前台包并导入到 `wwwroot/frontends/{manifest.id}` |
| `GET /api/frontends/{id}/package` | 导出前台包 zip |
| `PUT /api/frontends/{id}/layout?path={layout}` | 保存前台包里的 layout JSON |

ZIP 导入要求：

1. 压缩包根目录或第一层目录内必须有 `manifest.json`。
2. `manifest.json` 必须有 `id`。
3. 导入后会覆盖同 ID 的旧包。
4. 后端会拒绝路径穿越条目。

页面识别规则：

1. 优先使用 `manifest.pages[]`。
2. 如果没有 `pages`，扫描包内所有包含 `nodes` 数组的 `.json` 文件。
3. 如果仍然没有页面，使用 `entryLayout` 作为默认页。

## BP 示例页面

`bp-demo` 示例会展示：

| 组件 | 数据 |
| --- | --- |
| `bp-room-summary` | 房间名、阶段、回合、地图、比分 |
| `team-card` | 队伍名、当前阵营、选手、角色 |
| `bp-selection-list` | 求生者禁用、监管者禁用、地图禁用 |

示例页面可接收真实 BP 信息：

```text
/bp-layout?frontend=bp-demo&page=main&roomId=room-001
```

runtime 会用本地脚本 `/runtime/layout-renderer/vendor/signalr.min.js` 连接 SignalR，不依赖 CDN。

连接流程：

1. 读取 `/api/rooms/{roomId}` 或 `/api/rooms` 得到初始房间。
2. 连接 `/hubs/game`。
3. 调用 `JoinRoom(roomId)`。
4. 调用 `ReplaceSubscriptions(roomId, events)`。
5. 调用 `RequestRoomSnapshot(roomId)` 拉取完整快照。
6. 后续监听 `RoomEvent`，按 `eventType` 合并 payload 并刷新 bind。

页面右下角会显示当前连接状态：

| 状态 | 说明 |
| --- | --- |
| `SignalR connected` | 已连接并订阅房间事件 |
| `SignalR waiting` | 没有 roomId，正在等房间出现 |
| `SignalR failed` | SignalR 连接失败 |
| `REST polling` | 降级为 REST 轮询 |

对应 API：

```text
GET /api/rooms/room-001
GET /api/rooms
GET /api/signalr/events
SignalR /hubs/game
```

## 事件命名

当前后端已有事件：

```text
room.snapshot
room.info.updated
match.created
room.map.updated
room.ban.updated
room.global-ban.updated
room.role.selected
room.phase.updated
```

后续可以按设计文档逐步扩展语义事件：

```text
phase.pick.enter
phase.pick.leave
phase.scoreboard.enter
score.updated
character.selected
character.banned
map.selected
frontend.reset
frontend.animation.stopAll
```

原则：内核广播语义事件，layout 决定视觉响应，组件执行 action。
