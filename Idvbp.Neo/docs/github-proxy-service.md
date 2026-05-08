# GitHub 代理选择服务

## 目标

`GitHubProxyService` 用来统一处理应用内所有 GitHub 相关访问。

它解决的问题是：中国大陆用户访问 GitHub、GitHub API、Raw 文件、Release 附件或未来的更新包、组件包时可能不稳定。以后只要是应用内主动访问 GitHub 的功能，都应该通过这个服务生成最终 URL 或发起 HTTP 请求，而不是在各个功能里分别硬编码代理。

当前已经接入的场景：

- 设置页加载 GitHub 贡献者列表。
- 设置页打开项目 GitHub 仓库。
- 贡献者头像加载。

后续推荐接入的场景：

- 应用更新检查。
- 应用更新包下载。
- 前台组件包下载。
- GitHub Release 资源下载。
- Raw GitHub 内容下载。

## 相关文件

```text
Idvbp.Neo/
├─ github-proxies.json
├─ Server/
│  └─ Services/
│     └─ GitHubProxyService.cs
├─ ViewModels/
│  └─ Pages/
│     └─ SettingPageViewModel.cs
└─ Views/
   └─ Pages/
      └─ SettingPage.axaml

data/
└─ github-proxy-settings.json
```

### `github-proxies.json`

开发者维护的默认代理配置。

这个文件会随应用复制到输出目录，适合放内置代理列表。以后要新增、移除、禁用默认代理，优先改这个文件。

示例：

```json
{
  "defaultProxyId": "direct",
  "proxies": [
    {
      "id": "gh-proxy",
      "name": "gh-proxy.com",
      "urlTemplate": "https://gh-proxy.com/{url}",
      "enabled": true
    }
  ]
}
```

字段说明：

- `defaultProxyId`：默认选中的代理 ID。默认建议保持 `direct`，由用户在设置页自行切换。
- `proxies`：内置代理列表。
- `id`：稳定 ID，不要随便改，否则用户保存的选择会失效。
- `name`：设置页显示名称。
- `urlTemplate`：代理 URL 模板。
- `enabled`：是否启用该内置代理。

### `data/github-proxy-settings.json`

用户本地设置文件，由程序自动生成。

这个文件保存：

- 用户当前选择的代理。
- 用户在设置页添加的自定义代理。

开发者不要把这个文件当成默认配置源，也不要提交用户机器上的实际内容。

## URL 模板规则

代理地址推荐使用 `{url}` 占位符：

```text
https://gh-proxy.com/{url}
```

原始 URL：

```text
https://github.com/AyaSlinc/Idvbp.Neo
```

会被重写为：

```text
https://gh-proxy.com/https://github.com/AyaSlinc/Idvbp.Neo
```

如果模板不包含 `{url}`，服务会把原始 URL 拼到代理地址后面：

```text
https://example.com/proxy
```

会生成类似：

```text
https://example.com/proxy/https://github.com/AyaSlinc/Idvbp.Neo
```

因此更推荐显式写 `{url}`，避免不同代理服务的路径规则不一致。

## 直连模式

服务内置一个特殊代理：

```text
id: direct
name: 直连 GitHub
```

选择 `direct` 时，`RewriteUri` 会返回原始 URL，不做代理重写。

## 支持重写的 GitHub 域名

当前服务只重写 GitHub 相关域名，避免误伤普通外部请求。

支持范围：

- `github.com`
- `*.github.com`
- `raw.githubusercontent.com`
- `objects.githubusercontent.com`
- `github-releases.githubusercontent.com`

如果后续发现更新或下载链路还会跳转到其他 GitHub 资源域名，需要在 `GitHubProxyService.IsGitHubHost` 里补充。

## 服务注册

桌面端和内嵌 Web 服务都会注册 `IGitHubProxyService`。

桌面端注册位置：

```text
App.Services.cs
```

内嵌 ASP.NET Core 注册位置：

```text
ServerModule.cs
```

这意味着后续桌面 ViewModel、后台服务、API endpoint 都可以通过依赖注入拿到同一类能力。

## 代码使用方式

### 重写 URL

适合用于打开浏览器、生成下载地址、传给已有下载器。

```csharp
var url = _gitHubProxyService
    .RewriteUri("https://github.com/AyaSlinc/Idvbp.Neo")
    .ToString();
```

### 读取 JSON

适合访问 GitHub API。

```csharp
var contributors = await _gitHubProxyService.GetFromJsonAsync<GitHubContributor[]>(
    "https://api.github.com/repos/AyaSlinc/Idvbp.Neo/contributors",
    cancellationToken);
```

### 下载二进制内容

适合头像、Release 资源、组件包、更新包。

```csharp
var bytes = await _gitHubProxyService.GetByteArrayAsync(
    "https://github.com/example/repo/releases/download/v1.0.0/package.zip",
    cancellationToken);
```

## 设置页行为

设置页提供三类操作：

1. 从下拉框选择当前 GitHub 访问方式。
2. 查看当前使用状态。
3. 添加自定义代理。

自定义代理添加成功后会立即：

- 写入 `data/github-proxy-settings.json`。
- 自动切换到新代理。
- 刷新设置页代理列表。

## 后续功能接入规范

新增任何 GitHub 下载或更新功能时，遵守下面的规则：

1. 不要在业务功能里自己拼代理 URL。
2. 不要在业务功能里保存代理选择。
3. 不要为更新服务、组件下载服务各做一套代理配置。
4. 所有 GitHub URL 先交给 `IGitHubProxyService`。
5. 如果下载器必须自己持有 `HttpClient`，至少先使用 `RewriteUri` 得到最终地址。

推荐模式：

```text
业务服务拿到原始 GitHub URL
-> 调用 IGitHubProxyService.RewriteUri
-> 使用重写后的 URL 请求资源
```

如果业务服务本身只需要简单 GET，可以直接用：

```text
GetFromJsonAsync
GetByteArrayAsync
```

## 和普通反向代理的区别

项目里已有 `proxies.json` 和 `ReverseProxyMiddleware`，它们负责本地 Web 路由代理，例如：

- `/overlay`
- `/proxy`

`GitHubProxyService` 不是本地 Web 路由代理。

它负责的是“应用作为客户端访问 GitHub 时，选择哪个外部 GitHub 加速/代理服务”。

两者职责不同：

| 功能 | 负责内容 |
|---|---|
| `ReverseProxyMiddleware` | 浏览器访问本地服务时的路由代理 |
| `GitHubProxyService` | 应用访问 GitHub 时的出口 URL 选择 |

## 注意事项

- 公共 GitHub 代理服务稳定性不可控，内置列表应该保持容易修改。
- 不同代理对 API、Raw、Release 附件的支持程度不一样，添加默认代理前最好至少验证这三类 URL。
- 用户自定义代理只在本机生效，不应覆盖开发者默认配置。
- `id` 一旦发布后尽量不要修改。
- `urlTemplate` 应优先使用 HTTPS。

## 建议验证清单

添加或修改默认代理后，建议验证：

1. `https://github.com/AyaSlinc/Idvbp.Neo` 可以打开。
2. `https://api.github.com/repos/AyaSlinc/Idvbp.Neo/contributors` 可以返回 JSON。
3. `https://raw.githubusercontent.com/...` 类型 URL 可以下载。
4. GitHub Release 附件 URL 可以下载。
5. 设置页切换代理后，贡献者列表可以刷新。
