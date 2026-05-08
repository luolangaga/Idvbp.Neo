# 页面包商店部署与使用文档

## 功能范围

页面包商店用于把“页面管理”导出的 ZIP 页面包集中存放到一个 GitHub 仓库中。

当前支持：

- 从 GitHub 仓库读取页面包列表。
- 下载商店页面包。
- 下载安装到本机 `wwwroot/frontends`。
- 上传页面管理导出的 ZIP 页面包。
- 将本地已有页面包发布到商店。
- 下载和读取 GitHub 内容时对齐 `GitHubProxyService`，使用设置页里选择的 GitHub 代理。

商店后端不需要单独部署服务器。它使用 GitHub 仓库作为存储后端，通过 GitHub Contents API 拉取和推送文件。

## 关键代码位置

- `Server/Services/FrontendPackageStoreService.cs`
  - 页面包商店核心服务。
  - 负责读取 GitHub 仓库目录、下载 ZIP、安装 ZIP、上传 ZIP。

- `Server/FrontendPackageApiEndpoints.cs`
  - 页面包商店 REST API。

- `Views/Pages/WebProxyPage.axaml`
  - 桌面端“页面管理”页面。
  - 提供本地页面包管理和打开页面包商店入口。

- `Views/FrontendPackageStoreWindow.cs`
  - 独立页面包商店窗口。
  - 提供商店浏览、详情查看、下载安装、上传 ZIP、发布本地包。

- `Idvbp.Neo.Frontend/src/App.tsx`
  - 独立 Web 页面包商店页面。

- `docs/github-proxy-service.md`
  - GitHub 代理服务规范。
  - 页面包商店访问 GitHub 时必须遵守这份文档，不要在业务代码里自己拼代理地址。

## GitHub 仓库准备

建议为页面包商店单独创建一个 GitHub 仓库，例如：

```text
your-org/idvbp-neo-frontend-store
```

仓库结构建议：

```text
idvbp-neo-frontend-store/
└─ packages/
   ├─ bp-demo-1.0.0.zip
   └─ tournament-overlay-1.2.0.zip
```

`packages` 目录不是固定要求，可以在配置中修改。但建议保持一个专用目录，避免和 README、文档、脚本混在一起。

## Token 权限

只下载和安装页面包时，不需要 Token。

上传页面包到 GitHub 时需要 Token。

如果仓库是经典 Personal Access Token：

- 私有仓库需要 `repo` 权限。
- 公开仓库可使用能写入 Contents 的 token。

如果仓库使用 Fine-grained personal access token：

- Repository access：选择页面包商店仓库。
- Permissions：`Contents` 设置为 `Read and write`。

不要把真实 Token 提交到仓库。普通用户上传时不需要提前写配置；点击上传或发布时，程序会弹出提交窗口。

提交窗口支持：

- GitHub 快速授权：使用 OAuth Device Flow 打开 GitHub 授权页，授权成功后自动拿到 token。
- 保存本地授权：勾选后会写入 `data/frontend-package-store-auth.json`，下次上传可直接使用。
- 手动粘贴 Token：OAuth 没配置时仍可手动粘贴。
- 清除本地授权：删除本地保存的 token。

如果 Token 有目标分支直推权限，程序会直接上传 ZIP。

如果目标分支受保护或直推失败，程序会自动创建上传分支并提交 Pull Request。仓库管理员审核并合并 PR 后，页面包会同步出现在商店列表中。

## 应用配置

配置文件位置：

```text
Idvbp.Neo/appsettings.json
```

配置项：

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
- `Branch`：保存页面包的分支，默认 `main`。
- `Path`：保存 ZIP 的目录，默认 `packages`。
- `Token`：可选的上传用 Token。普通用户建议留空，上传时在提交窗口填写。
- `OAuthClientId`：可选。配置后上传窗口会启用“GitHub 快速授权”。需要在 GitHub OAuth App 中开启 Device Flow。

自动化部署或开发调试时，也可以用环境变量提供 Token：

```powershell
$env:IDVBP_NEO_GITHUB_TOKEN="ghp_xxx"
```

也支持：

```powershell
$env:GITHUB_TOKEN="ghp_xxx"
```

上传时 Token 读取优先级：

1. 上传提交窗口快速授权或手动填写的 Token
2. 本地保存的 `data/frontend-package-store-auth.json`
3. `FrontendPackageStore:Token`
4. `IDVBP_NEO_GITHUB_TOKEN`
5. `GITHUB_TOKEN`

## OAuth 快速授权配置

如果希望用户不用自己生成 Token，可以配置 GitHub OAuth App 的 Device Flow。

配置步骤：

1. 在 GitHub 创建 OAuth App。
2. 开启 Device Flow。
3. 把 OAuth App 的 Client ID 写入：

```json
{
  "FrontendPackageStore": {
    "OAuthClientId": "你的 GitHub OAuth Client ID"
  }
}
```

用户上传时点击“GitHub 快速授权”，程序会：

1. 向 GitHub 请求设备验证码。
2. 打开 GitHub 授权页面。
3. 自动复制验证码。
4. 轮询授权结果。
5. 授权成功后得到 access token。
6. 用户勾选保存时，把 token 存到本地 `data/frontend-package-store-auth.json`。

本地保存的 token 只在用户机器上生效，不要提交到仓库。

## GitHub 代理要求

页面包商店访问 GitHub 时应使用 `IGitHubProxyService`。

已接入的访问：

- 读取仓库目录：`https://api.github.com/repos/{owner}/{repo}/contents/{path}`
- 下载 ZIP：GitHub 返回的 `download_url`
- 上传 ZIP：GitHub Contents API `PUT /contents/{path}`

用户如果在设置页切换 GitHub 代理，页面包商店会跟随该代理设置。

不要在页面包商店业务代码中手动拼接 `gh-proxy.com/{url}` 这类地址。新增 GitHub 访问时，应先调用 `IGitHubProxyService.RewriteUri` 或使用 `GetFromJsonAsync` / `GetByteArrayAsync`。

## 启动与验证

### 桌面端使用

启动 Idvbp.Neo 后，进入：

```text
页面管理 -> 页面包商店
```

如果配置正确，会显示：

```text
商店仓库：owner/repository · branch/path
```

点击“刷新商店”会读取 GitHub 仓库目录中的 ZIP 文件。

### Web 页面使用

开发模式下进入前端目录：

```powershell
cd Idvbp.Neo.Frontend
npm run dev -- --host 127.0.0.1
```

打开：

```text
http://127.0.0.1:5173
```

前端开发服务器已配置 `/api` 代理到：

```text
http://localhost:5000
```

因此需要先启动 Idvbp.Neo 内置后端。

### API 验证

查看商店配置状态：

```http
GET /api/frontend-package-store/status
```

获取商店页面包列表：

```http
GET /api/frontend-package-store/packages
```

未配置 `Owner` 或 `Repository` 时，接口会返回配置错误。

## 用户如何下载页面包

桌面端操作：

1. 打开“页面管理”。
2. 点击右上角“页面包商店”。
3. 在弹出的商店窗口左侧选择页面包。
4. 在右侧查看页面包简介、入口文件、页面列表、组件文件数和资源文件数。
5. 点击“下载安装”。
6. 安装成功后回到页面管理，点击“刷新”。
7. 在本地页面包列表中点击“打开窗口”进行预览。

Web 页面操作：

1. 打开页面包商店 Web 页面。
2. 在商店页面包列表中搜索或浏览。
3. 点击“安装”下载安装到本机。
4. 点击“下载”仅下载 ZIP 文件。

安装会调用：

```http
POST /api/frontend-package-store/packages/{fileName}/install
```

直接下载会调用：

```http
GET /api/frontend-package-store/packages/{fileName}/download
```

## 用户如何上传页面管理导出的 ZIP

适用场景：用户已经通过页面管理导出了一个页面包 ZIP，希望上传到 GitHub 商店。

前置条件：

- 已配置 `Owner`、`Repository`、`Branch`、`Path`。
- 已配置 OAuth 快速授权，或手里有 GitHub Token。不需要提前写进配置文件。
- ZIP 内必须包含 `manifest.json`。
- `manifest.json` 必须包含 `id`。

桌面端小白步骤：

1. 打开“页面管理”。
2. 点击右上角“页面包商店”。
3. 在商店窗口右上角点击“上传 ZIP”。
4. 选择页面管理导出的 `.zip` 文件。
5. 程序会弹出提交窗口。
6. 推荐点击“GitHub 快速授权”，按浏览器提示完成授权。
7. 如果没有配置 OAuth，就手动粘贴 GitHub Token。
8. 可勾选“保存授权到本地，下次上传不再询问”。
9. 点击“提交”。
10. 程序会先尝试直接上传到商店分支。
11. 如果没有直推权限或分支受保护，程序会自动提交 Pull Request。
12. 如果提交了 PR，把 PR 地址发给仓库管理员审核。
13. 管理员合并 PR 后，点击“刷新”即可看到新页面包。

Web 页面操作：

1. 打开页面包商店 Web 页面。
2. 点击“上传 zip”。
3. 选择页面管理导出的 `.zip` 文件。
4. 按提示粘贴 GitHub Token。
5. 等待上传完成；如果生成 PR，等待管理员合并。

上传接口：

```http
POST /api/frontend-package-store/packages/upload
Content-Type: multipart/form-data
```

表单字段：

```text
file: 页面包 ZIP 文件
token: 本次提交使用的 GitHub Token
```

## 用户如何发布本地页面包

适用场景：页面包已经安装在本机 `wwwroot/frontends` 中，希望直接发布到 GitHub 商店。

桌面端操作：

1. 打开“页面管理”。
2. 点击右上角“页面包商店”。
3. 在商店窗口左下角“发布本地页面包”中选择目标页面包。
4. 点击“发布”。
5. 在提交窗口点击 GitHub 快速授权，或手动粘贴 GitHub Token。
6. 可选择保存授权到本地。
7. 点击“提交”。
8. 程序会把本地包重新打成 ZIP。
9. 程序会先尝试直接上传；如果失败，会自动创建 PR。
10. PR 合并后，刷新商店即可看到页面包。

Web 页面操作：

1. 打开页面包商店 Web 页面。
2. 在左侧“本地页面包”中选择目标包。
3. 点击“发布本地包”。

发布接口：

```http
POST /api/frontend-package-store/packages/upload-local/{id}
Content-Type: application/json
```

其中 `{id}` 是本地页面包 ID。

请求体：

```json
{
  "token": "ghp_xxx"
}
```

## 文件命名规则

上传 ZIP 时，服务会读取 `manifest.json`。

文件名优先使用：

```text
{manifest.id}-{manifest.version}.zip
```

如果 `version` 为空，则使用：

```text
{manifest.id}.zip
```

示例：

```json
{
  "id": "asg-director-bp",
  "name": "ASG 导播 BP",
  "version": "1.0.0"
}
```

上传后文件名：

```text
asg-director-bp-1.0.0.zip
```

如果同名文件已存在，上传会更新该文件。

## 页面包 ZIP 要求

ZIP 中必须包含：

```text
manifest.json
```

典型结构：

```text
my-package.zip
└─ my-package/
   ├─ manifest.json
   ├─ layout.json
   ├─ components/
   └─ assets/
```

也支持 `manifest.json` 位于 ZIP 根目录：

```text
my-package.zip
├─ manifest.json
├─ layout.json
├─ components/
└─ assets/
```

导入逻辑会自动识别 `manifest.json` 所在目录，并把页面包解压到：

```text
wwwroot/frontends/{manifest.id}
```

## REST API 列表

### 商店状态

```http
GET /api/frontend-package-store/status
```

返回：

```json
{
  "configured": true,
  "owner": "your-org",
  "repository": "idvbp-neo-frontend-store",
  "branch": "main",
  "path": "packages",
  "canUpload": true,
  "repositoryUrl": "https://github.com/your-org/idvbp-neo-frontend-store/tree/main/packages"
}
```

### 商店页面包列表

```http
GET /api/frontend-package-store/packages
```

只返回 `.zip` 文件。

### 下载安装

```http
POST /api/frontend-package-store/packages/{fileName}/install
```

### 直接下载 ZIP

```http
GET /api/frontend-package-store/packages/{fileName}/download
```

### 上传 ZIP

```http
POST /api/frontend-package-store/packages/upload
Content-Type: multipart/form-data
```

表单字段：

```text
file: 页面包 ZIP 文件
token: 本次提交使用的 GitHub Token
```

### 发布本地页面包

```http
POST /api/frontend-package-store/packages/upload-local/{id}
Content-Type: application/json
```

请求体：

```json
{
  "token": "ghp_xxx"
}
```

## 常见问题

### 页面显示“商店仓库未配置”

检查 `appsettings.json`：

- `FrontendPackageStore:Owner` 是否为空。
- `FrontendPackageStore:Repository` 是否为空。
- 当前运行目录下的配置文件是否是你修改的那份。

发布后运行时，配置文件可能来自输出目录：

```text
bin/Debug/net10.0/appsettings.json
```

### 可以下载，不能上传

通常是 Token 未填写、Token 权限不足，或 PR 分支也无法创建。

检查：

- 提交窗口是否粘贴了 Token。
- Token 是否有目标仓库 Contents 写权限。
- 目标分支是否受保护。
- 如果目标分支受保护，Token 是否至少允许在仓库创建分支和 PR。

### 刷新商店失败

常见原因：

- `Owner` 或 `Repository` 写错。
- `Path` 目录不存在。
- 私有仓库没有读取权限。
- 当前 GitHub 代理不可用。

可以先在设置页切换 GitHub 访问方式，再回到页面管理刷新商店。

### 上传后商店列表没有变化

可能原因：

- 上传到了另一个分支。
- 上传到了另一个 `Path`。
- GitHub API 缓存尚未刷新。
- 页面仍显示旧数据。

处理方式：

1. 点击“刷新商店”。
2. 打开 GitHub 仓库确认文件路径。
3. 检查 `FrontendPackageStore:Branch` 和 `FrontendPackageStore:Path`。

### ZIP 安装失败，提示缺少 manifest.json

页面包 ZIP 必须包含 `manifest.json`。

建议使用页面管理已有的导出功能生成 ZIP，不要手动压缩单个零散文件。

### 下载很慢或失败

页面包商店下载走 `GitHubProxyService`。

可以进入设置页：

1. 切换 GitHub 访问方式。
2. 确认贡献者列表或 GitHub 测试资源可以加载。
3. 回到页面管理重新下载。

## 维护建议

- 商店仓库只存放经过确认可用的 ZIP 页面包。
- 上传前先在本机安装并打开默认页确认可用。
- `manifest.id` 发布后不要随意修改，否则用户安装后会变成另一个本地页面包。
- 建议每次更新页面包时递增 `manifest.version`。
- 不要把 Token 写入公开仓库。
- 新增任何 GitHub 访问逻辑时，继续使用 `IGitHubProxyService`。
