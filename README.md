# 吃土小幫手

這是以 Discord UID 管理代購訂單的 Discord Bot、ASP.NET Core API 與 Vue 網頁。

## 本機啟動

後端需要 .NET SDK 10。未設定資料庫或 Discord token 時，API、Bot 與 migration 會保持停用，但程式仍可啟動，方便先跑規則測試。

```powershell
dotnet test DiscordBrokeBot.sln
dotnet run --project src/DiscordBrokeBot
```

前端需要 Node 22 與 pnpm 11：

```powershell
pnpm --dir frontend install
pnpm --dir frontend dev
```

將 `frontend/.env.example` 複製為本機環境設定，並確認 `VITE_API_BASE_URL` 指向後端。

## 設定

部署用設定放在 `deploy/.env`，不要加入版控。必要鍵值請見 `deploy/.env.example`。後端使用 ASP.NET Core Cookie Authentication、Data Protection key volume、DbUp 與 MariaDB；不把 Discord access token 寫入資料庫。

正式環境部署請參考 [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md)。

## 專案結構

- `src/DiscordBrokeBot`: 單一 ASP.NET Core 程式，包含 API、Bot、OAuth、訂單服務、Dapper Store 與 migration。
- `tests/DiscordBrokeBot.Tests`: 不需要外部服務即可執行的規則測試。
- `frontend`: Vue 3、TypeScript、Vite SPA。
- `deploy`: Docker image、Compose 與環境變數範本。
- `docs/EF_CORE_MIGRATION.md`: 未來若要改用 EF Core，必須保留的資料與交易行為。

正式環境的 MariaDB 目前沒有備份與還原保證，這是第一版已知風險。
