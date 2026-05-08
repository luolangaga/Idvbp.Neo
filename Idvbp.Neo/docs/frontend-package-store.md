# 页面包商店部署与使用

页面包商店把“页面管理”导出的 ZIP 页面包集中存放到一个 GitHub 仓库里。应用不需要额外部署后端服务，读取、上传、创建分支和创建 PR 都直接调用 GitHub API。

## 仓库结构

建议为商店单独创建一个仓库，例如：

```text
your-org/idvbp-neo-frontend-store
```

推荐结构如下：

```text
idvbp-neo-frontend-store/
└── packages/
    └── asg-director-bp/
        ├── package.zip
        └── store.json
```

每个页面包一个目录，目录名就是 `packageId`。`package.zip` 是页面管理导出的 ZIP，`store.json` 是商店展示信息。

`store.json` 示例：

```json
{
  "packageId": "asg-director-bp",
  "pageId": "bp",
  "name": "ASG 导播 BP",
  "description": "适合赛事 BP 导播的大屏页面包。",
  "authorName": "作者名字",
  "screenshotUrl": "https://example.com/screenshot.png",
  "website": "https://example.com",
  "contact": "email@example.com"
}
```

上传前程序会弹窗让用户填写这些信息，然后把 `package.zip` 和 `store.json` 一起提交到 `packages/{packageId}/`。

## 应用配置

配置文件位置：

```text
Idvbp.Neo/appsettings.json
```

配置示例：

```json
{
  "FrontendPackageStore": {
    "Owner": "your-org",
    "Repository": "idvbp-neo-frontend-store",
    "Branch": "main",
    "Path": "packages",
    "Token": "",
    "OAuthClientId": ""
  }
}
```

字段说明：

- `Owner`：GitHub 用户名或组织名。
- `Repository`：商店仓库名，不包含 owner。
- `Branch`：商店主分支，默认 `main`。
- `Path`：包目录，默认 `packages`。
- `Token`：可选。普通用户建议留空，上传时再授权。
- `OAuthClientId`：可选。配置后上传窗口会启用“GitHub 快速授权”。

自动化部署或开发调试时，也可以用环境变量提供 Token：

```powershell
$env:IDVBP_NEO_GITHUB_TOKEN="ghp_xxx"
```

也支持：

```powershell
$env:GITHUB_TOKEN="ghp_xxx"
```

上传 Token 读取优先级：

1. 上传窗口快速授权或手动填写的 Token
2. 本地保存的 `data/frontend-package-store-auth.json`
3. `FrontendPackageStore:Token`
4. `IDVBP_NEO_GITHUB_TOKEN`
5. `GITHUB_TOKEN`

## GitHub API 与代理

GitHub API 不走 `GitHubProxyService`，包括：

- 读取 `contents` 目录
- 上传 `package.zip`
- 上传 `store.json`
- 创建分支
- 创建 Pull Request
- OAuth Device Flow 授权

只有实际下载 GitHub 返回的 `download_url` 时会使用 `GitHubProxyService`，用于改善 ZIP 下载成功率。不要手动拼接 `gh-proxy.com/{url}` 这类代理地址。

## OAuthClientId 申请

如果希望用户不用自己生成 Token，可以创建 GitHub OAuth App 并启用 Device Flow。

步骤：

1. 打开 GitHub。
2. 进入 `Settings -> Developer settings -> OAuth Apps`。
3. 点击 `New OAuth App`。
4. `Application name` 填应用名，例如 `Idvbp.Neo Page Store`。
5. `Homepage URL` 可以填项目主页或仓库地址，例如 `https://github.com/your-org/idvbp-neo-frontend-store`。
6. `Authorization callback URL` 对桌面端 Device Flow 不会实际使用，但 GitHub 要求填写，可以填 `http://localhost/`。
7. 创建后复制 `Client ID`。
8. 在 OAuth App 设置里启用 `Device Flow`。
9. 把 `Client ID` 写入 `FrontendPackageStore:OAuthClientId`。

配置完成后，用户上传时点击“GitHub 快速授权”，程序会打开 GitHub 授权页并复制验证码。授权成功后会拿到 access token。用户勾选保存时，token 会写入本机：

```text
data/frontend-package-store-auth.json
```

这个文件只在用户本机生效，不要提交到仓库。

## Token 权限

只下载和安装页面包不需要 Token。上传需要 Token。

Fine-grained personal access token 推荐权限：

- Repository access：选择页面包商店仓库。
- Contents：`Read and write`。
- Pull requests：如果需要自动创建 PR，设置为 `Read and write`。

经典 Personal Access Token：

- 私有仓库通常需要 `repo`。
- 公共仓库至少需要可写 Contents 的权限。

如果 Token 可以直接写入目标分支，程序会直接提交。若目标分支受保护或直推失败，程序会自动创建上传分支并提交 Pull Request。管理员审核合并 PR 后，商店刷新即可看到新页面包。

## 用户如何下载页面包

桌面端步骤：

1. 打开“页面管理”。
2. 点击“页面包商店”。
3. 在商店窗口左侧选择页面包。
4. 右侧查看名称、简介、作者、页面列表、文件数量等信息。
5. 点击“下载安装”。
6. 下载窗口会展示下载进度、已下载大小、总大小、速度和耗时。
7. 安装完成后回到“页面管理”，点击刷新。
8. 在本地页面包列表中打开或预览。

商店详情会先使用 `store.json` 立即展示简介、作者、截图和网站。ZIP 里的 `manifest.json` 与页面结构会在后台继续解析，包很大时不会阻塞整个商店窗口。

## 用户如何上传 ZIP

适用于已经通过“页面管理”导出 ZIP 的情况。

前置条件：

- 已配置 `Owner`、`Repository`、`Branch`、`Path`。
- 已配置 OAuth 快速授权，或用户手里有 GitHub Token。
- ZIP 内必须包含 `manifest.json`。
- `manifest.json` 必须包含 `id`。

小白步骤：

1. 打开“页面管理”。
2. 点击“页面包商店”。
3. 点击“上传 ZIP”。
4. 选择页面管理导出的 `.zip` 文件。
5. 在“预览页面包文件”窗口检查 `manifest.json` 信息和 ZIP 文件列表。
6. 确认无误后继续。
7. 在“填写商店信息”窗口填写：
   - 包 ID
   - 页面 ID
   - 名称
   - 简介
   - 作者
   - 截图 URL
   - 网站
   - 联系方式
8. 点击“下一步”。
9. 推荐点击“GitHub 快速授权”。
10. 浏览器打开后输入验证码并授权。
11. 回到应用，按需勾选“保存授权到本地”。
12. 点击“提交”。
13. 程序会打开上传进度窗口，显示当前阶段、进度条、耗时和估算速度。
14. 如果直推成功，商店会立即出现新包。
15. 如果生成 PR，把 PR 地址发给仓库管理员审核。
16. 管理员合并后，刷新商店即可看到新包。

## 用户如何发布本地页面包

适用于页面包已经安装在本机 `wwwroot/frontends` 的情况。

步骤：

1. 打开“页面管理”。
2. 点击“页面包商店”。
3. 在左下角“发布本地页面包”里选择目标包。
4. 点击“发布”。
5. 填写商店信息。
6. 使用 GitHub 快速授权或手动粘贴 Token。
7. 点击“提交”。
8. 程序会重新打包本地页面包，并上传到 `packages/{packageId}/`。
9. 如果生成 PR，等待管理员合并。

## REST API

商店状态：

```http
GET /api/frontend-package-store/status
```

页面包列表：

```http
GET /api/frontend-package-store/packages
```

查看详情：

```http
GET /api/frontend-package-store/packages/{packageId}
```

下载安装：

```http
POST /api/frontend-package-store/packages/{packageId}/install
```

直接下载 ZIP：

```http
GET /api/frontend-package-store/packages/{packageId}/download
```

上传 ZIP：

```http
POST /api/frontend-package-store/packages/upload
Content-Type: multipart/form-data
```

表单字段：

```text
file: 页面包 ZIP 文件
token: 本次提交使用的 GitHub Token，可为空但必须已有本地保存授权或配置 Token
metadata: store.json 对应的 JSON 字符串
```

发布本地页面包：

```http
POST /api/frontend-package-store/packages/upload-local/{id}
Content-Type: application/json
```

请求体：

```json
{
  "token": "ghp_xxx",
  "metadata": {
    "packageId": "asg-director-bp",
    "pageId": "bp",
    "name": "ASG 导播 BP",
    "description": "适合赛事 BP 导播的大屏页面包。",
    "authorName": "作者名字",
    "screenshotUrl": "https://example.com/screenshot.png",
    "website": "https://example.com",
    "contact": "email@example.com"
  }
}
```

## 常见问题

### 页面显示“商店仓库未配置”

检查 `appsettings.json`：

- `FrontendPackageStore:Owner` 是否为空。
- `FrontendPackageStore:Repository` 是否为空。
- 当前运行目录下的配置文件是否是你修改的那份。

调试运行时，实际配置可能来自：

```text
bin/Debug/net10.0/appsettings.json
```

### 可以下载但不能上传

通常是 Token 缺失、权限不足，或目标分支受保护。

检查：

- 是否已完成 OAuth 授权或粘贴 Token。
- Token 是否有目标仓库 Contents 写权限。
- 如果需要自动 PR，Token 是否有创建分支和 Pull Request 的权限。
- 目标分支是否受保护。

### 上传后商店没有变化

可能原因：

- 生成了 PR 但还没有合并。
- 上传到了另一个分支。
- 上传到了另一个 `Path`。
- 商店页面还在显示旧数据。

处理方式：

1. 查看上传结果是否包含 PR 地址。
2. 打开 GitHub 仓库确认路径是否为 `packages/{packageId}/package.zip` 和 `packages/{packageId}/store.json`。
3. 确认 PR 已合并到配置的 `Branch`。
4. 回到页面包商店点击刷新。

### ZIP 安装失败，提示缺少 manifest.json

页面包 ZIP 必须包含 `manifest.json`。建议使用页面管理内置导出功能生成 ZIP，不要手动压缩零散文件。

## 维护建议

- 商店仓库只存放确认可用的页面包。
- 上传前先在本机安装并打开默认页面确认可用。
- `packageId` 发布后不要随意修改，否则用户安装后会变成另一个包。
- 更新页面包时建议递增 `manifest.version`。
- 不要把真实 Token 写入公开仓库。
