# 正式環境部署

本專案的正式環境分成兩個部分：

- Vue 前端：Cloudflare Pages，從 GitHub `main` 分支自動建置。
- ASP.NET Core API 與 Discord Bot：Docker Server 上的同一個 container，由 Nginx 反向代理。
- MariaDB：沿用 Docker Server 上既有的 MariaDB，不由本專案 Compose 建立。

以下假設正式網址為：

- 前端：`https://chitu.konnokai.me`
- API：`https://chitu-api.konnokai.me`

若實際網域不同，請同步修改 Cloudflare、Discord OAuth、`deploy/.env` 與 Nginx 設定。

## 1. Discord 設定

在 Discord Developer Portal 的 OAuth2 Redirects 新增：

```text
https://chitu-api.konnokai.me/auth/callback
```

Bot 邀請 scope 使用 `bot` 與 `applications.commands`，權限可維持 `0`。準備好以下三個值，但不要放進 Git：

- Bot Token
- OAuth Client ID
- OAuth Client Secret

## 2. Docker Server

Docker Server 需要 Docker、Docker Compose、Nginx，以及可被 Docker host 連線的 MariaDB。

```sh
git clone https://github.com/konnokai/DiscordBrokeBot.git
cd DiscordBrokeBot
cp deploy/.env.example deploy/.env
chmod 600 deploy/.env
```

編輯 `deploy/.env`，至少填入：

```dotenv
ConnectionStrings__Default=Server=host.docker.internal;Port=3306;Database=discord_broke_bot;User ID=discord_broke_bot;Password=CHANGE_ME;
Discord__BotToken=CHANGE_ME
Discord__ClientId=CHANGE_ME
Discord__ClientSecret=CHANGE_ME
Auth__FrontendBaseUrl=https://chitu.konnokai.me
Auth__PublicApiBaseUrl=https://chitu-api.konnokai.me
```

MariaDB 必須允許這個帳號從 Docker host／Docker bridge 連入；不要把資料庫密碼寫進 `appsettings.json`、文件或 shell history。

第一次部署前先備份 `discord_broke_bot` database。啟動時 DbUp 會執行 embedded migrations，並在 MariaDB 的 `SchemaVersions` 表記錄已執行腳本；不需要手動插入 migration 紀錄。

```sh
docker compose --env-file deploy/.env -f deploy/compose.yaml up -d --build
docker compose --env-file deploy/.env -f deploy/compose.yaml ps
curl --fail http://127.0.0.1:5080/health
```

後續更新：

```sh
git pull --ff-only origin main
docker compose --env-file deploy/.env -f deploy/compose.yaml up -d --build
docker compose --env-file deploy/.env -f deploy/compose.yaml logs --tail=100 backend
```

`chitu-data-protection` volume 不可刪除，否則既有登入 Cookie、OAuth state 與快速登入 token 的保護金鑰會失效。

## 3. Nginx

複製 `deploy/nginx/chitu-api.konnokai.me.conf` 到：

```text
/etc/nginx/sites-available/chitu-api.konnokai.me.conf
```

建立啟用連結並重新載入：

```sh
sudo ln -s /etc/nginx/sites-available/chitu-api.konnokai.me.conf /etc/nginx/sites-enabled/chitu-api.konnokai.me.conf
sudo nginx -t
sudo systemctl reload nginx
```

設定假設既有憑證位於：

```text
/root/.lego/certificates/chitu-api.konnokai.me.crt
/root/.lego/certificates/chitu-api.konnokai.me.key
```

若憑證檔名或目錄不同，先修改設定檔。憑證申請與續期不由本專案處理；續期完成後需 reload Nginx。

## 4. Cloudflare DNS 與 Pages

### API DNS

在 Cloudflare 建立 `chitu-api.konnokai.me` 的 A／AAAA record 指向 Docker Server，Proxy 開啟。SSL/TLS 模式使用 `Full (strict)`，Origin 憑證需有效。

### Pages 專案

在 Cloudflare Pages 建立或連結 GitHub 專案：

- Repository：`konnokai/DiscordBrokeBot`
- Production branch：`main`
- Root directory：`frontend`
- Build command：`corepack enable && pnpm install --frozen-lockfile && pnpm build`
- Output directory：`dist`
- Environment variable：`NODE_VERSION=22`
- Environment variable：`VITE_API_BASE_URL=https://chitu-api.konnokai.me`
- Optional environment variable：`VITE_DISCORD_INVITE_URL=<Discord 安裝連結>`

新增 Pages Custom Domain：

```text
chitu.konnokai.me
```

前端沒有 top-level `404.html`，Cloudflare Pages 會自動啟用 SPA fallback，因此 `/orders/...`、`/quick-login`、`/privacy` 與 `/tos` 重新整理時仍會回到 Vue entrypoint。不要新增 `/* /index.html 200` 的 `_redirects` 規則，Wrangler 會將它判定為無限迴圈。

## 5. 驗證清單

```sh
curl --fail https://chitu-api.konnokai.me/health
curl --fail -I https://chitu.konnokai.me/
```

瀏覽器驗證：

1. 從前端使用 Discord 登入。
2. 確認 callback 回到 `https://chitu.konnokai.me/oauth/callback` 後進入訂單頁。
3. 執行 `/order link`，確認快速登入連結可使用一次。
4. 從前端變更購買／收款／款項，確認另一方收到 Discord 私訊。
5. 確認訂單操作紀錄出現在明細頁。

查看服務 log：

```sh
docker compose --env-file deploy/.env -f deploy/compose.yaml logs -f backend
```

正式環境目前沒有本專案 database 的自動備份與還原流程，資料庫備份仍由 Docker Server／MariaDB 維運流程負責。
