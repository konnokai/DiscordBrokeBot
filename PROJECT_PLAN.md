# 吃土小幫手 Discord Bot 專案計畫

> 文件狀態：待專案負責人審查  
> 更新日期：2026-08-20  
> 專案目錄：`DiscordBrokeBot`

## 1. 專案目標

吃土小幫手用來管理 Discord 使用者之間的代購訂單。

請求方在 Discord 伺服器中建立訂單並指定代購方。代購方可以從 Discord 或網頁查看訂單、更新購買狀態、管理款項紀錄，雙方也能編輯訂單內容。網頁使用 Discord OAuth 登入，不另外建立帳號與密碼。

第一版使用 Discord global application command，並允許任何 Discord 伺服器公開安裝 Bot。所有已安裝 Bot 的伺服器都能使用指令。

## 2. 名詞定義

| 名詞 | 英文程式名稱 | 定義 |
| --- | --- | --- |
| 請求方 | `Requester` | 建立訂單、請別人協助購買的人。 |
| 代購方 | `Buyer` | 被指定負責購買，並負責確認購買與收款狀態的人。 |
| 訂單總額 | `OrderTotal` | 新台幣單價乘以數量。 |
| 款項紀錄 | `PaymentEntry` | 代購方登記的收款、退款或沖銷紀錄。金額可正可負。 |
| 已收款總額 | `ReceivedTotal` | 一筆訂單所有款項紀錄的加總。 |
| 差額 | `Balance` | 訂單總額減去已收款總額，可為負數。 |
| 收款完成模式 | `SettlementMode` | 自動判定、強制完成或強制未完成。 |
| 封存 | `Archive` | 將訂單從一般清單隱藏，但保留資料並允許復原。 |

## 3. 已確認需求

- 後端、Bot 與資料存取使用 C#。
- 後端採用 `.NET 10` 與 ASP.NET Core。
- Discord Bot 使用 Discord.Net。
- 資料存取使用 Dapper 與 MySqlConnector。
- 資料庫使用現有的 MariaDB 10.11。
- 網頁使用 Vue 3、TypeScript 與 Vite。
- 後端與 Bot 放在同一個 ASP.NET Core 程式與 Docker container。
- Vue 部署至 Cloudflare Pages。
- 前端使用 `https://chitu.konnokai.me`，API 使用 `https://chitu-api.konnokai.me`。
- API 透過 Docker 主機上的 Nginx 對外提供 HTTPS。
- Discord 操作結果使用 ephemeral 限定訊息。
- Discord Application 開放公開 Guild Install，指令採 global 註冊。
- 訂單不依 Discord 伺服器隔離，授權以 Discord UID 為準。
- 來源伺服器 UID 與建立當下的伺服器名稱仍會保存及顯示。
- 使用者離開來源伺服器後，仍可管理原有訂單。
- 請求方可以指定自己為代購方。
- Discord 管理員沒有額外的訂單管理權限。
- 全站只使用新台幣整數。
- 外幣、匯差與手續費由使用者先換算後填入新台幣單價。
- 網頁第一版不提供建立訂單，只能從 Discord 建立。
- Bot 與網頁第一版只提供台灣繁體中文介面。

## 4. 第一版範圍

### 4.1 包含

- Discord Slash 指令建立訂單。
- Discord ephemeral 互動清單。
- Discord 按鈕、選單與 Modal 操作。
- Discord 私訊通知。
- 公開 Bot 安裝連結與 global application command。
- 公開服務需要的隱私政策與服務條款。
- 公開服務的基本防濫用措施。
- Discord OAuth 登入。
- 請求方與代購方的訂單總表。
- 訂單查看、編輯、封存與復原。
- 代購方更新已購買狀態。
- 代購方新增、編輯與刪除款項紀錄。
- 代購方封鎖或解除封鎖特定請求方。
- 自動與人工覆寫的收款完成狀態。
- 全部、已購買、未購買、已收款與差額統計。
- 桌面與手機版網頁。
- Docker、Nginx、Cloudflare Pages 與 MariaDB 部署。

### 4.2 不包含

- 網頁建立訂單。
- Discord 管理員介入訂單。
- 多幣別與匯率換算。
- 不可竄改的會計帳本。
- Discord 私訊保證送達與自動重送。
- 多後端執行個體。
- Redis Session、快取或分散式鎖。
- JWT。
- 微服務、CQRS、MediatR 與事件匯流排。
- 永久刪除訂單。
- 英文或其他語言介面。

## 5. 系統架構

```text
Discord 使用者
    │ Discord Gateway、Slash 指令、元件互動
    ▼
ASP.NET Core 10 單一容器
├─ Discord.Net Bot
├─ Discord OAuth
├─ Web API
├─ 訂單與權限服務
├─ Dapper Store
└─ DbUp Migration
    │
    ▼
現有 MariaDB 10.11

Cloudflare Pages
└─ Vue 3 SPA
    │ HTTPS、Cookie、CSRF Token
    ▼
chitu-api.konnokai.me
    │
    ▼
主機 Nginx 1.26.3
    │
    ▼
ASP.NET Core 容器
```

Bot 與 API 不透過 HTTP 互相呼叫。兩者直接使用同一個訂單服務，確保權限、金額與狀態規則只實作一次。

## 6. 技術選擇

### 6.1 後端

- `.NET 10`
- ASP.NET Core
- Discord.Net
- Dapper
- MySqlConnector
- DbUp MySQL
- ASP.NET Core Cookie Authentication
- ASP.NET Core Data Protection
- ASP.NET Core OpenAPI
- xUnit

### 6.2 前端

- Vue 3
- TypeScript
- Vite
- Vue Router
- Font Awesome
- 原生 `fetch`
- ESLint
- Vue TypeScript typecheck

第一版不加入 Pinia、Axios 與大型 UI component framework。頁面狀態先由 Vue component 與 composable 管理，有明確的跨頁共享需求再加入狀態管理工具。

### 6.3 不採用 EF Core 的原因

目前 Pomelo 的穩定版只對應到 EF Core 9。`.NET 8` 雖然可搭配 Pomelo EF Core 8，但官方支援將於 2026-11-10 結束，不適合當作新專案的基礎。

第一版採用 `.NET 10 + Dapper + MySqlConnector`。所有 SQL 必須集中在 Store 類別，避免散落在 Bot、API 或 Vue 契約中，保留未來切換 EF Core 的空間。

參考資料：[.NET and .NET Core official support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

### 6.4 Redis 決策

遠端環境已有 Redis，但第一版不使用。

目前只有一個後端執行個體。Cookie 可以由 ASP.NET Core Data Protection 驗證，Discord 互動狀態也能放在 custom ID 與資料庫中，因此 Redis 不會解決現階段的必要問題。

出現下列需求時再加入 Redis：

- 後端改為多執行個體。
- 需要立即撤銷特定登入 Session。
- Discord 私訊需要可靠排程與重送。
- 需要跨執行個體鎖定。
- 有量測證據顯示資料庫查詢需要快取。

## 7. 專案結構

```text
DiscordBrokeBot.sln
PROJECT_PLAN.md
README.md
src/
  DiscordBrokeBot/
    Api/
    Auth/
    Bot/
    Features/
      Orders/
        Models/
        OrderService.cs
        OrderStore.cs
        PaymentEntryStore.cs
        UserBlockStore.cs
    Infrastructure/
      Database/
      Discord/
    Migrations/
    Program.cs
frontend/
  src/
    api/
    components/
    composables/
    pages/
    router/
    types/
tests/
  DiscordBrokeBot.Tests/
deploy/
  compose.yaml
  Dockerfile
  .env.example
docs/
  EF_CORE_MIGRATION.md
```

後端正式程式只有一個專案。測試獨立成一個專案。不建立只有單一實作的 Repository interface、Application layer 或 Domain layer class library。

## 8. 核心流程

### 8.1 建立訂單

1. 請求方在任一已安裝 Bot 的 Discord 伺服器執行 `/order add`。
2. Discord 提交請求方 UID、代購方 UID、來源伺服器與訂單欄位。
3. Bot 驗證輸入並呼叫共用的 `OrderService`。
4. 後端寫入訂單。
5. Bot 以 ephemeral 訊息回覆建立結果。
6. Bot 嘗試私訊代購方，內容包含請求方、物品、數量、總額、攤位、備註與網頁連結。
7. 私訊失敗時保留訂單，並在 ephemeral 回覆提示請求方。

### 8.2 網頁登入

1. 使用者從 Cloudflare Pages 開啟網頁。
2. Vue 將瀏覽器導向 API 的 Discord OAuth 登入端點。
3. API 產生 OAuth state 與 PKCE 資料後導向 Discord。
4. Discord 將授權碼送回 API callback。
5. API 驗證 state，交換授權碼並取得 Discord UID 與基本帳號資料。
6. API 建立加密且簽章的登入 Cookie。
7. API 將瀏覽器導回前端。
8. Vue 使用 `credentials: include` 呼叫 API。
9. API 從驗證後的 Cookie claims 取得 Discord UID。

### 8.3 管理訂單

1. 使用者從 Discord ephemeral 清單或網頁選擇訂單。
2. API 或 Bot 從已驗證的 Discord 身分取得操作者 UID。
3. Store 在同一個 SQL 或資料庫交易中套用 UID、封存狀態與操作條件。
4. 找不到符合條件的資料時，回覆「找不到該筆訂單，可能已被封存或你沒有操作權限。」
5. 成功後重新取得該頁資料與統計，更新畫面。

### 8.4 款項與收款完成

1. 代購方新增一筆有正負號的款項與事由。
2. 後端鎖定訂單並寫入款項紀錄。
3. 後端重新計算已收款總額與有效收款完成狀態。
4. 交易提交後，Bot 嘗試通知請求方。
5. 同一筆新增同時造成收款完成時，只傳送一則合併通知。

## 9. 資料模型

### 9.1 `orders`

| 欄位 | 說明 |
| --- | --- |
| `id` | 訂單流水號。API 與 Discord custom ID 以字串傳遞。 |
| `requester_discord_user_id` | 請求方 Discord UID。 |
| `requester_display_name` | 建立當下的請求方名稱快照。 |
| `buyer_discord_user_id` | 代購方 Discord UID。建立後不可更換。 |
| `buyer_display_name` | 建立當下的代購方名稱快照。 |
| `source_guild_id` | 建立訂單的 Discord 伺服器 UID。 |
| `source_guild_name` | 建立當下的伺服器名稱快照。 |
| `item_name` | 物品名稱。必填。 |
| `unit_price` | 每件物品的新台幣單價，不含小數且必須大於零。 |
| `quantity` | 數量，必須大於零。 |
| `note` | 備註。必填。 |
| `stall` | 攤位名稱或編號。選填。 |
| `is_purchased` | 是否已購買。 |
| `purchased_at` | 最近一次標記已購買的 UTC 時間；取消時清空。 |
| `settlement_override` | 空值代表自動判定；其餘代表強制完成或強制未完成。 |
| `created_at` | 建立 UTC 時間。 |
| `updated_at` | 最近更新 UTC 時間。 |
| `archived_at` | 封存 UTC 時間；空值代表有效訂單。 |
| `archived_by_discord_user_id` | 執行封存的 Discord UID。復原時清空。 |

不儲存 `order_total`。後端統一以 `unit_price * quantity` 計算，避免單價或數量修改後產生不一致。

### 9.2 `payment_entries`

| 欄位 | 說明 |
| --- | --- |
| `id` | 款項流水號。 |
| `order_id` | 所屬訂單。 |
| `amount` | 有正負號的新台幣整數，不可為零。 |
| `reason` | 事由。必填。 |
| `created_at` | 建立 UTC 時間。 |
| `updated_at` | 最近更新 UTC 時間。 |

金額規則：

- 正數代表代購方收到款項。
- 負數代表退款、沖銷或其他扣回。
- 零元紀錄沒有餘額效果，第一版不接受。
- 款項紀錄可由代購方編輯或永久刪除，第一版不保留刪除稽核紀錄。

### 9.3 `user_blocks`

| 欄位 | 說明 |
| --- | --- |
| `buyer_discord_user_id` | 執行封鎖的代購方 Discord UID。 |
| `requester_discord_user_id` | 被封鎖的請求方 Discord UID。 |
| `requester_display_name` | 封鎖當下的請求方名稱快照。 |
| `created_at` | 建立 UTC 時間。 |

代購方與請求方 UID 組成唯一鍵。封鎖關係跨 Guild 生效。

### 9.4 索引

至少需要支援下列查詢方向：

- 依代購方 UID 取得有效或封存訂單。
- 依請求方 UID 取得有效或封存訂單。
- 依訂單取得款項紀錄。
- 依建立時間排序訂單。
- 建立訂單前依代購方與請求方 UID 檢查封鎖關係。

實際索引以這些查詢的 `WHERE` 與排序欄位建立，不為尚未出現的查詢預先加索引。

### 9.5 API 數值格式

- Discord UID 一律以字串傳給 Vue。
- 訂單與款項 ID 一律以字串傳給 Vue。
- 金額一律以十進位字串傳給 Vue，避免 JavaScript 整數精度問題。
- 數量可使用 JSON number。
- 時間使用 ISO 8601 UTC 字串。

## 10. 金額與狀態規則

```text
訂單總額 = 單價 × 數量
已收款總額 = SUM(款項紀錄金額)
差額 = 訂單總額 - 已收款總額
```

- 差額大於零：請求方仍有應付金額。
- 差額等於零：金額剛好結清。
- 差額小於零：代購方目前多收，或帳上存在需要處理的調整。
- 收款完成與已購買是兩個獨立狀態。
- 尚未購買也可以先登記款項。
- 單價與數量都必須大於零。
- 單價或數量修改後，訂單總額與自動收款狀態立即重算。
- 訂單已有款項後，雙方仍可修改單價與數量，並立即重算總額與自動收款狀態。

收款完成模式：

| 模式 | 規則 |
| --- | --- |
| 自動 | 已收款總額大於或等於訂單總額時完成。 |
| 強制完成 | 不論金額都顯示完成。 |
| 強制未完成 | 不論金額都顯示未完成。 |

人工覆寫不會因新增、編輯或刪除款項而自動消失。代購方必須主動切回自動模式。

## 11. 權限規則

| 操作 | 請求方 | 代購方 | 其他使用者 | Discord 管理員 |
| --- | --- | --- | --- | --- |
| 查看有效訂單 | 是 | 是 | 否 | 無額外權限 |
| 查看封存訂單 | 是 | 是 | 否 | 無額外權限 |
| 編輯物品、單價、數量、備註、攤位 | 是 | 是 | 否 | 無額外權限 |
| 變更請求方 | 否 | 否 | 否 | 否 |
| 變更代購方 | 否 | 否 | 否 | 否 |
| 標記已購買或未購買 | 否 | 是 | 否 | 否 |
| 新增、編輯、刪除款項 | 否 | 是 | 否 | 否 |
| 調整收款完成模式 | 否 | 是 | 否 | 否 |
| 封鎖或解除封鎖請求方 | 否 | 是 | 否 | 否 |
| 無款項時封存或復原 | 是 | 是 | 否 | 否 |
| 有款項時封存或復原 | 否 | 是 | 否 | 否 |

補充規則：

- 自己建立給自己時，同一個 UID 同時具有請求方與代購方權限，以權限較高的代購方規則處理。
- 封存中的訂單不可編輯、切換購買狀態或管理款項，必須先復原。
- 權限不依目前 Discord 伺服器成員資格判定。
- 權限不依 Discord role 或 Administrator 權限判定。

## 12. Discord Bot 設計

### 12.1 Slash 指令

第一版提供下列入口：

```text
/order add
/order list
/order block
/order unblock
/order blocked
```

canonical name 可保留英文，使用 Discord `zh-TW` localization 顯示台灣繁體中文名稱與說明。

`/order add` 參數：

| Canonical name | 繁體中文名稱 | 繁體中文說明 | 必填 |
| --- | --- | --- | --- |
| `buyer` | 代購方 | 選擇負責購買此訂單的 Discord 使用者，不接受 Bot 帳號。 | 是 |
| `item` | 物品名稱 | 輸入需要代購的物品名稱。 | 是 |
| `unit-price` | 單價 | 輸入大於零的每件新台幣單價。外幣請先換算並包含手續費。 | 是 |
| `quantity` | 數量 | 輸入需要購買的數量。 | 是 |
| `note` | 備註 | 輸入此訂單的必要說明。 | 是 |
| `stall` | 攤位 | 輸入販售攤位名稱或編號。 | 否 |

### 12.2 互動清單

`/order list` 回覆 ephemeral 訊息，提供：

- 「我要代購」清單。
- 「我的委託」清單。
- 「已封存」清單。
- 訂單選擇器。
- 上一頁與下一頁。
- 查看詳情。
- 編輯訂單。
- 標記已購買或未購買。
- 查看及管理款項。
- 切換收款完成模式。
- 封存或復原。

元件只顯示操作者可用的按鈕，但後端仍必須在 SQL 中檢查操作者 UID。ephemeral 是介面隱私，不是伺服器端授權。

不需要額外執行一次純授權查詢。更新與刪除 SQL 直接包含 UID 與封存條件；受影響筆數為零時回覆找不到或無權限。

需要讀取現有內容的 Modal，例如訂單編輯，會以包含 UID 條件的查詢載入資料。

### 12.3 款項 Modal

- 金額欄位預設留白。
- 接受正整數與負整數。
- 不設定只能大於零的前端限制。
- 事由必填。
- 後端再次驗證格式、非零金額與代購方權限。

### 12.4 封鎖請求方

- `/order block requester` 由代購方封鎖指定請求方。
- `/order unblock requester` 解除封鎖。
- `/order blocked` 以 ephemeral 清單顯示目前封鎖名單，並提供解除封鎖按鈕。
- 封鎖關係跨 Guild 生效。
- 被封鎖的請求方不能再建立以該使用者為代購方的新訂單。
- 封鎖不改變既有訂單、款項與雙方原有權限。
- 封鎖及解除封鎖不通知對方。
- 不允許封鎖自己。

### 12.5 Global command 註冊

- Application command 採 global 註冊，不維護 Guild allowlist。
- 所有已安裝 Bot 的 Discord 伺服器都能使用指令。
- Global command 更新後的生效時間由 Discord 控制，部署流程不假設會立即出現。
- 訂單建立後不再依 Guild 成員資格授權。

### 12.6 公開安裝

- Discord Application 開放 Guild Install。
- 安裝 scope 使用 `bot` 與 `applications.commands`。
- Bot 安裝權限設為 `0`，不要求任何 Guild permission。Interaction response 與私訊不需要 Administrator、Manage Guild、Manage Messages 或一般頻道發話權限。
- 邀請連結公開提供，不維護 Guild allowlist 或人工核准清單。
- Bot 只要求功能實際需要的 Discord 權限，不要求 Administrator。
- 新安裝的 Guild 不需要建立個別設定即可使用訂單指令。
- 公開上線前必須提供隱私政策與服務條款。
- 防止惡意建單與私訊騷擾的規則採跨 Guild 封鎖與第 15.6 節的 rate limit。

## 13. 網頁設計

### 13.1 頁面

- 登入頁。
- 公開邀請入口 `/invite`，導向 Discord Guild Install。
- 隱私政策 `/privacy`。
- 服務條款 `/tos`。
- OAuth callback 過渡頁或載入狀態。
- 「我要代購」總表。
- 「我的委託」總表。
- 「已封存」總表。
- 「封鎖名單」管理頁。
- 訂單詳情與編輯介面。
- 款項紀錄管理介面。

### 13.2 總表內容

- 對方 Discord 名稱快照。
- 來源伺服器名稱快照。
- 物品名稱。
- 攤位。
- 單價。
- 數量。
- 訂單總額。
- 已購買狀態。
- 已收款總額。
- 差額。
- 有效收款完成狀態。
- 收款完成模式。
- 建立與更新時間。

### 13.3 摘要數字

- 全部訂單總額。
- 未購買訂單總額。
- 已購買訂單總額。
- 已收款總額。
- 差額總計。

「我要代購」可將差額標示為應收；「我的委託」可將差額標示為應付。計算都由後端完成，Vue 不自行重算財務摘要。

### 13.4 互動規則

- 請求方與代購方都能編輯一般訂單欄位。
- 只有代購方看得到購買狀態、款項與收款完成的操作按鈕。
- 封存訂單只提供查看與復原。
- 網頁不提供建立訂單。
- 代購方可查看、封鎖及解除封鎖請求方；封鎖不改變既有訂單。
- API 失敗時保留使用者已輸入內容並顯示可理解的錯誤。
- 手機版不依賴寬表格，改用可讀的欄位排列或清單布局。
- 前端介面不使用 emoji 當作按鈕、狀態、導覽或裝飾圖示。需要圖示時統一使用 Font Awesome，避免明顯的 AI Slop 風格；使用者輸入與 Discord 名稱原有的 emoji 維持原樣。

### 13.5 視覺方向

- 主題採「暖色紙本帳本」，不是科技儀表板。
- 背景使用暖米白 `#F3EEE5`，主要內容底色使用 `#FFFDF8`，文字使用深褐 `#2B2621`。
- 主色使用陶土色 `#9F4F35`，已購買與已完成使用墨綠 `#2F6B4F`，待處理使用赭黃 `#9A6A1F`，錯誤使用暗紅 `#A23B3B`，分隔線使用 `#D7CBBE`。
- 中文使用系統黑體字型堆疊，金額使用 `ui-monospace`，不額外載入流行 Google Font。
- 第一版 Logo 使用「吃土小幫手」文字標誌搭配 Font Awesome `fa-receipt`，不製作低品質的自繪吉祥物。
- 產品識別元素是一條類似收據撕線的摘要分隔線，用在總額區，不重複套用到所有區塊。
- 版面以清楚的清單、表格與留白為主，不使用紫藍漸層、發光按鈕、浮動卡片、滿版格線、膠囊標籤或卡片套卡片。
- 動畫只用於狀態變更，並支援 `prefers-reduced-motion`。

## 14. API 契約

初版端點規劃：

| Method | Path | 用途 |
| --- | --- | --- |
| `GET` | `/auth/login` | 開始 Discord OAuth。 |
| `GET` | `/auth/callback` | 處理 Discord OAuth callback。 |
| `POST` | `/auth/logout` | 清除登入 Cookie。 |
| `GET` | `/api/auth/me` | 取得目前登入使用者。 |
| `GET` | `/api/auth/csrf` | 取得修改資料所需的 CSRF token。 |
| `GET` | `/api/orders` | 依角色、狀態與封存條件取得訂單及摘要。 |
| `GET` | `/api/orders/{id}` | 取得訂單詳情與款項紀錄。 |
| `PATCH` | `/api/orders/{id}` | 編輯一般訂單欄位。 |
| `PUT` | `/api/orders/{id}/purchase-status` | 由代購方切換購買狀態。 |
| `PUT` | `/api/orders/{id}/settlement-mode` | 由代購方設定自動、完成或未完成。 |
| `POST` | `/api/orders/{id}/archive` | 封存訂單。 |
| `POST` | `/api/orders/{id}/restore` | 復原訂單。 |
| `POST` | `/api/orders/{id}/payment-entries` | 新增款項紀錄。 |
| `PATCH` | `/api/payment-entries/{id}` | 編輯款項金額與事由。 |
| `DELETE` | `/api/payment-entries/{id}` | 刪除款項紀錄。 |
| `GET` | `/api/blocks` | 取得目前登入者的封鎖名單。 |
| `POST` | `/api/blocks/{requesterId}` | 封鎖指定請求方。 |
| `DELETE` | `/api/blocks/{requesterId}` | 解除封鎖。 |
| `GET` | `/health` | Container 與 Nginx health check。 |

不提供 `POST /api/orders`。Discord Bot 在同一個程式內直接呼叫 `OrderService` 建立訂單。

所有修改端點都需要 Cookie 驗證與 CSRF token。API 不接受前端傳入操作者 UID。

## 15. 身分驗證與安全

### 15.1 為什麼不用 JWT

JWT 是 token 的一種格式，不是登入驗證的必要條件。

Discord OAuth callback 完成後，ASP.NET Core Cookie Authentication 會建立受 Data Protection 加密與簽章保護的登入票證。瀏覽器之後自動將 HttpOnly Cookie 傳給 API，後端驗證後建立 `ClaimsPrincipal`，再從 claim 取得 Discord UID。

Vue 不接觸 Discord access token，也不把登入 token 存入 `localStorage`。

### 15.2 Cookie

- `Secure`。
- `HttpOnly`。
- 使用符合前後端同根網域部署方式的 `SameSite` 設定。
- Cookie 只提供給 API 網域。
- Cookie 採四小時絕對有效期限，不使用 sliding expiration。
- Data Protection key 存入 Docker volume。
- 登出以 `POST` 執行並驗證 CSRF。

### 15.3 OAuth

- 使用 Authorization Code flow。
- 驗證 OAuth state。
- 使用 PKCE。
- scope 只要求 `identify`。
- 取得 Discord UID 與顯示資料後，不持久化 Discord access token。
- callback 完成後只允許導回固定的前端網址，避免 open redirect。

### 15.4 CORS 與 CSRF

- CORS 只允許正式 Cloudflare Pages 自訂網域。
- API 允許該來源使用 credentials。
- 修改資料的請求必須帶 CSRF header。
- Cloudflare preview 網域不預設加入正式 CORS allowlist。

### 15.5 Discord 元件授權

ephemeral 只能限制元件顯示對象，不能取代後端授權。

最小做法是將操作者 UID 直接放進同一個 SQL 條件：

```sql
UPDATE orders
SET ...
WHERE id = @OrderId
  AND archived_at IS NULL
  AND (
    requester_discord_user_id = @ActorId
    OR buyer_discord_user_id = @ActorId
  );
```

代購方專屬操作必須使用 `buyer_discord_user_id = @ActorId`。這不增加額外查詢，也不信任 custom ID 內的訂單編號。

### 15.6 Rate limit

第一版使用 ASP.NET Core 與 `System.Threading.RateLimiting` 的記憶體內 partitioned limiter，不使用 Redis。限制依使用者實際操作成本分層：

| 範圍 | 分區依據 | 限制 |
| --- | --- | --- |
| `/auth/login`、`/auth/callback` | 來源 IP | 每分鐘 10 次。 |
| 已登入的 Web API 查詢 | Discord UID | 每分鐘 120 次。 |
| 已登入的 Web API 修改 | Discord UID | 每分鐘 30 次。 |
| Discord `/order add` | 請求方 Discord UID | 每分鐘 5 次。 |
| 其他 Discord 修改操作 | 操作者 Discord UID | 每分鐘 30 次。 |

- 超過限制的 Web API 回覆 HTTP `429` 與 `Retry-After`。
- Discord 操作超過限制時使用 ephemeral 訊息提示稍後再試。
- IP 只從受信任的 Cloudflare 與 Nginx forwarded headers 取得，不接受任意來源偽造。
- 單一後端重啟會清空 limiter 狀態，第一版接受這個限制。
- 上線後依實際 Log 調整數值，不在沒有量測資料時加入更多限制層級。

## 16. 資料一致性與併發

- 訂單一般欄位採欄位式更新，不用前端送回整個資料列。
- 款項新增、編輯與刪除使用資料庫交易。
- 款項交易中鎖定所屬訂單，再計算修改前後的有效收款狀態。
- 請求方封存訂單時，在同一個交易中確認目前沒有款項紀錄。
- 封存與新增款項不可因同時執行而繞過權限規則。
- 建立訂單時必須在寫入前檢查代購方是否已封鎖請求方；命中封鎖時不建立訂單，也不傳送私訊。
- 第一版不加入 Redis lock。
- 第一版不加入整列 optimistic concurrency token；若實測出現多人同時編輯互相覆蓋，再加入版本欄位與條件更新。

## 17. Discord 通知

| 事件 | 收件人 | 是否通知 |
| --- | --- | --- |
| 建立訂單 | 代購方 | 是 |
| 新增正數款項 | 請求方 | 是 |
| 新增負數款項 | 請求方 | 是 |
| 編輯款項 | 請求方 | 否，除非因此轉為完成。 |
| 刪除款項 | 請求方 | 否，除非因此轉為完成。 |
| 收款狀態轉為完成 | 請求方 | 是 |
| 收款狀態轉為未完成 | 請求方 | 否 |
| 編輯一般訂單欄位 | 對方 | 否 |
| 封存或復原 | 對方 | 否 |

同一次新增款項若同時造成收款完成，只傳送一則合併通知。

請求方與代購方為同一 UID 時，不傳送自我通知。

私訊在資料庫交易提交後傳送。私訊失敗不回滾資料，只寫入 Log 並在當次 Discord 操作中提示操作者。第一版不建立 outbox 與自動重送。

## 18. Migration 與未來 EF Core 遷移

### 18.1 第一版

- 使用 DbUp 管理版本化 SQL。
- Migration script 內嵌於後端 assembly。
- 後端啟動時只對自己的 database 執行尚未套用的 migration。
- DbUp journal 保留已執行版本。
- 應用程式資料庫帳號只授權自己的 database，不取得其他 database 權限。

### 18.2 程式碼註解要求

- `OrderStore` 與 `PaymentEntryStore` 必須有 XML `<summary>` 與 `<remarks>`。
- `<remarks>` 要說明目前使用 Dapper、SQL 集中位置與 EF Core 遷移文件。
- MariaDB 特有 SQL，例如 `SELECT ... FOR UPDATE`、受影響筆數授權與聚合查詢，要加註為何需要。
- 註解說明遷移時不能遺失的行為，不逐行重述 SQL。
- 不在每個方法加入沒有資訊量的 `TODO: migrate to EF Core`。

### 18.3 `docs/EF_CORE_MIGRATION.md`

文件至少記錄：

- 既有 DbUp schema 如何建立 EF Core migration baseline。
- Discord UID 的 MariaDB 型別與 C# 型別。
- 金額有正負號且沒有小數的 mapping。
- `settlement_override` 的可空值語意。
- UTC 時間處理。
- 封存資料的 query filter。
- 欄位式更新與原子授權條件。
- 款項交易與 row lock 行為。
- 聚合總額與差額查詢。
- 切換後必須維持通過的整合測試。

未來遷移 EF Core 時，優先替換 Store 內部，不改 Discord、API 與 Vue 契約。

## 19. 部署計畫

### 19.1 已確認環境

- Docker 主機：Debian 13 x86_64。
- Docker：28.5.1。
- Docker Compose：2.40.0。
- Nginx：1.26.3。
- MariaDB container：MariaDB 10.11。
- MariaDB Compose project：`mariadb`。
- 本機開發環境：.NET SDK 10.0.303、Node 22.23.1、pnpm 11.15.1。
- 本機沒有 Docker CLI，Docker build 與整合驗證改在 SSH 主機執行。

### 19.2 Compose

`deploy/compose.yaml` 第一版只管理後端服務與 Data Protection volume，不重建現有 MariaDB。

後端不加入現有 MariaDB 的 Docker network。Linux Compose 使用 `host-gateway`，讓 container 內的 `host.docker.internal` 指向 Docker 主機：

```yaml
extra_hosts:
  - "host.docker.internal:host-gateway"
```

MariaDB 連線主機使用 `host.docker.internal`，連接主機既有的 MariaDB port。API port 只綁定主機 loopback，再由 Nginx 轉送，不直接暴露到外部網路。

### 19.3 `.env`

`compose.yaml` 使用：

```yaml
env_file:
  - .env
```

實際 `deploy/.env`：

- 只存在 Docker 主機。
- 不進版控。
- 限檔案擁有者讀取。
- 不在 Log、測試輸出或文件中顯示值。

版控只保存 `deploy/.env.example`，列出需要的鍵：

```dotenv
ASPNETCORE_ENVIRONMENT=
ASPNETCORE_URLS=
BACKEND_PORT=
ConnectionStrings__Default=
Discord__BotToken=
Discord__ClientId=
Discord__ClientSecret=
Auth__FrontendBaseUrl=https://chitu.konnokai.me
Auth__PublicApiBaseUrl=https://chitu-api.konnokai.me
```

Discord Application 建立完成後，由專案負責人手動填入 `Discord__BotToken`、`Discord__ClientId` 與 `Discord__ClientSecret`。

### 19.4 Nginx

- Nginx 設定檔放在 `/etc/nginx/sites-enabled/chitu-api.konnokai.me.conf`。
- `chitu-api.konnokai.me` 終止 HTTPS。
- 轉送到 loopback 上的後端 port。
- 傳遞正確的 `X-Forwarded-For`、`X-Forwarded-Proto` 與 `Host`。
- ASP.NET Core 只信任部署環境中的 forwarded headers。
- OAuth callback 使用公開 HTTPS API 網址產生。
- Discord OAuth callback 固定為 `https://chitu-api.konnokai.me/auth/callback`。
- 既有憑證位於 `/root/.lego/certificates/`，本專案不新增或變更憑證申請與更新流程。
- `chitu-api.konnokai.me` 由 Cloudflare Proxy 對外提供服務。
- `/health` 可由主機或部署流程檢查。

### 19.5 Cloudflare Pages

- Pages project 名稱為 `discord-broke-bot-frontend`。
- 正式部署 branch 使用 `main`。
- 建置目錄為 `frontend`。
- 使用 pnpm lockfile。
- 正式環境設定 `VITE_API_BASE_URL=https://chitu-api.konnokai.me`。
- SPA route 設定 fallback。
- OAuth callback 最終導回正式前端子網域。
- 隱私政策網址為 `https://chitu.konnokai.me/privacy`。
- 服務條款網址為 `https://chitu.konnokai.me/tos`。
- 公開安裝入口為 `https://chitu.konnokai.me/invite`。
- Preview deployment 不預設取得正式 API 的 Cookie 權限。

### 19.6 備份現況

- 現有 MariaDB 沒有備份流程，新 database 也不具備還原能力。
- 第一版不把備份列為已完成項目，不宣稱能從主機、volume 或人為誤刪中復原。
- 自動備份未納入本次已確認範圍；公開上線前需接受資料可能永久遺失的風險。

## 20. Logging 與健康檢查

- 使用 ASP.NET Core 內建 structured console logging。
- 不為第一版加入 Serilog、OpenTelemetry 或 Redis logging sink。
- 不記錄 Discord token、OAuth secret、Cookie、CSRF token 或資料庫密碼。
- Log 記錄訂單 ID、動作、結果與操作者 Discord UID，但不記錄完整備註內容。
- Discord 私訊失敗記錄事件與 Discord 錯誤類型。
- `/health` 至少確認後端程式可回應。
- 資料庫連線錯誤由啟動 migration 與實際 API 操作明確回報，不在第一版加入額外監控套件。

## 21. 測試計畫

### 21.1 後端單元測試

- 訂單總額計算。
- 正負款項加總。
- 差額為正、零與負數。
- 自動完成判定。
- 強制完成與強制未完成。
- 切回自動模式。
- 請求方與代購方權限矩陣。
- 自己建立給自己時的權限。
- 有款項後請求方不可封存。
- 封存訂單不可修改。
- 單價為零或負數時拒絕建立及更新。
- 封鎖、自我封鎖、解除封鎖與跨 Guild 封鎖規則。

### 21.2 MariaDB 整合測試

- DbUp 可從空 database 建立 schema。
- Dapper mapping 與 MariaDB 型別正確。
- UID、金額與 UTC 時間不失真。
- SQL 授權條件不會修改其他人的訂單。
- 款項與封存交易在併發時維持規則。
- 聚合摘要與明細一致。
- 款項紀錄刪除後永久移除且不再計入總額。
- 已封鎖的請求方無法建立新訂單。
- Integration test 使用獨立測試 database 或臨時 MariaDB container，不操作正式資料。

### 21.3 API 測試

- 未登入請求被拒絕。
- Cookie claims 可取得 Discord UID。
- CSRF 缺失或錯誤時拒絕修改。
- CORS 只允許設定的前端來源。
- 無權限與不存在的訂單不洩漏資料。
- API 的 UID、ID 與金額使用字串格式。
- 所有修改端點符合權限矩陣。
- Cookie 在登入四小時後失效，且不因持續操作延長。
- Web API 超過 rate limit 時回覆 `429` 與 `Retry-After`。

### 21.4 Discord 測試

- `/order add` 所有必填與選填參數。
- 台灣繁體中文指令名稱與說明。
- ephemeral 清單、分頁、選單、按鈕與 Modal。
- 權限不同時顯示正確元件。
- 元件 custom ID 被替換或訂單已封存時不會修改錯誤資料。
- 新增訂單與款項的私訊。
- 私訊關閉時資料仍成功保存。
- Discord Bot 帳號不能被指定為代購方。
- `/order block`、`/order unblock` 與 `/order blocked`。
- 被封鎖的請求方無法建立新訂單，既有訂單不受影響。
- Discord 操作超過 rate limit 時收到 ephemeral 提示。
- Global command 可在所有已安裝 Bot 的 Guild 使用。
- 公開邀請連結可將 Bot 安裝到新的測試 Guild，且不需要 allowlist 設定。

### 21.5 前端測試

- `pnpm build`。
- ESLint。
- Vue TypeScript typecheck。
- Discord OAuth 真實登入。
- 「我要代購」、「我的委託」與「已封存」切換。
- 編輯訂單。
- 正負款項與必填事由。
- 購買與收款完成模式。
- 封存與復原。
- 封鎖名單與解除封鎖。
- 暖色紙本帳本視覺、Font Awesome 圖示與收據分隔線。
- 錯誤訊息與輸入保留。

前端可執行後，使用 OpenChamber Web 實際檢查：

- 桌面 viewport。
- 手機 viewport。
- 真實 Discord 登入。
- Console error。
- Network error。
- 水平溢位與操作元件可用性。
- 色彩對比、鍵盤操作與 `prefers-reduced-motion`。

### 21.6 部署驗證

- 透過 SSH 在遠端 Docker 主機建置 Linux image。
- Container 可透過 `host.docker.internal` 連接現有 MariaDB。
- Migration 可套用到測試 database。
- Nginx 可代理 API 與 OAuth callback。
- Cloudflare Pages 可帶 Cookie 呼叫 API。
- Container restart 後資料保留。
- Container restart 後既有 Cookie 仍可由持久化 Data Protection key 驗證。

## 22. 實作順序

### 階段 1：專案骨架

- 建立 solution、ASP.NET Core 專案、測試專案與 Vue 專案。
- 加入格式、lint、typecheck 與基本 build。
- 建立設定模型與 `.env.example`。
- 驗證本機後端與前端 build。

### 階段 2：資料與規則

- 建立 DbUp migration。
- 建立 Order 與 PaymentEntry model。
- 建立 Dapper Store。
- 實作訂單、權限、金額與收款完成規則。
- 實作 UserBlock 與建立訂單前的封鎖檢查。
- 完成單元與 MariaDB 整合測試。

### 階段 3：Discord 建立流程

- 啟動 Discord.Net Gateway。
- 註冊 Discord global application command。
- 設定公開 Guild Install、必要 scope 與最小 Discord 權限。
- 實作 `/order add` 與繁體中文 localization。
- 拒絕將 Discord Bot 帳號指定為代購方。
- 套用 Discord 操作 rate limit。
- 實作建立後的 ephemeral 回覆與代購方私訊。

### 階段 4：Discord 管理流程

- 實作 `/order list`。
- 實作清單分頁、選擇器、按鈕與 Modal。
- 實作編輯、購買、款項、收款完成、封存與復原。
- 實作封鎖、解除封鎖與封鎖名單。
- 實作請求方通知。

### 階段 5：OAuth 與 API

- 實作 Discord OAuth。
- 實作 Cookie、Data Protection、CORS 與 CSRF。
- 實作查詢、修改、款項與封存 API。
- 實作封鎖 API、四小時 Cookie 與 Web API rate limit。
- 產生並檢查 OpenAPI contract。

### 階段 6：Vue 網頁

- 實作登入與登入狀態。
- 實作三個訂單區域與摘要。
- 實作詳情、編輯、款項與狀態操作。
- 實作封鎖名單。
- 套用暖色紙本帳本視覺與 Font Awesome。
- 完成手機與桌面布局。
- 使用 OpenChamber Web 驗證。

### 階段 7：部署

- 建立 production Dockerfile 與 Compose。
- 在現有 MariaDB 建立專用 database 與帳號。
- 在 SSH Docker 主機建置與啟動。
- 設定 Nginx API 子網域。
- 部署 Cloudflare Pages。
- 設定 Discord OAuth callback 與 global application command。
- 發布 Bot 安裝連結、隱私政策與服務條款。

### 階段 8：端到端驗收

- 使用指定測試 Guild 建立真實訂單。
- 使用請求方與代購方兩個 Discord 帳號驗證權限。
- 驗證網頁、Bot、資料庫與私訊同步。
- 驗證封存、復原、正負款項與自動覆寫狀態。
- 驗證 restart 與錯誤處理，並確認目前沒有資料庫還原能力。

## 23. 驗收條件

- 請求方可在任一已安裝 Bot 的 Guild 以 Slash 指令建立完整訂單。
- 未預先設定的 Guild 可透過公開邀請連結安裝 Bot，並在 global command 生效後使用指令。
- 公開安裝 scope 為 `bot` 與 `applications.commands`，Bot permission 為 `0`。
- 指令參數與說明使用台灣繁體中文 localization。
- Bot 保存請求方、代購方、來源伺服器與所有訂單欄位。
- 代購方能收到新增訂單私訊；私訊失敗不影響訂單。
- 代購方登入網頁後能看到所有 Guild 中指派給自己的訂單。
- 請求方登入網頁後能看到自己建立的訂單。
- 使用者離開來源 Guild 後仍能操作原有訂單。
- 雙方可編輯一般訂單欄位，但不可變更雙方身分。
- 單價與數量必須大於零；已有款項後仍允許雙方修改並立即重算。
- 只有代購方能更新購買狀態與款項。
- 款項支援正數、負數與必填事由，不接受零元。
- 刪除款項紀錄會永久移除，不保留稽核資料。
- 自動、強制完成、強制未完成與切回自動都符合規則。
- 摘要金額由後端計算，正確顯示全部、購買、收款與差額。
- 無款項時雙方可封存與復原；有款項時只有代購方可操作。
- Discord 端可透過 ephemeral 清單與 Modal 完成網頁可做的管理操作。
- 代購方可跨 Guild 封鎖或解除封鎖請求方；封鎖後的新訂單會被拒絕，既有訂單不受影響。
- Discord Bot 帳號不能被指定為代購方。
- 未授權 UID 無法查看或修改其他人的訂單。
- Cookie、CSRF、CORS 與 OAuth state 驗證有效。
- 登入 Cookie 採四小時絕對有效期限。
- Web API 與 Discord 操作依文件中的 rate limit 回覆正確結果。
- 網頁可在桌面與手機正常使用，無阻斷操作的 Console error。
- 後端可在遠端 Docker 主機連接現有 MariaDB。
- 前端使用 `https://chitu.konnokai.me`，API 使用 `https://chitu-api.konnokai.me`。
- `https://chitu.konnokai.me/privacy` 與 `https://chitu.konnokai.me/tos` 可正常開啟。
- Container restart 後訂單、款項與登入金鑰不遺失。

## 24. 已定案項目與部署責任

- 前端網址：`https://chitu.konnokai.me`。
- API 網址：`https://chitu-api.konnokai.me`。
- Discord OAuth callback：`https://chitu-api.konnokai.me/auth/callback`。
- Discord 安裝 scope：`bot`、`applications.commands`。
- Discord Bot permission：`0`。
- 公開安裝入口：`https://chitu.konnokai.me/invite`。
- 隱私政策：`https://chitu.konnokai.me/privacy`。
- 服務條款：`https://chitu.konnokai.me/tos`。
- Cloudflare Pages project：`discord-broke-bot-frontend`。
- 正式 branch：`main`。
- Nginx 設定目錄：`/etc/nginx/sites-enabled`。
- 既有憑證目錄：`/root/.lego/certificates/`，本專案不處理憑證申請與更新。
- 程式完成後，由專案負責人將 Discord token、Client ID 與 Client Secret 寫入遠端 `.env`。
- 現有 MariaDB 沒有備份，第一版不具備資料還原保證。

## 25. 刻意延後項目

- 若需要保證通知送達，再加入 outbox、Redis queue 與重送策略。
- 若需要多後端執行個體，再加入 Redis Session、分散式鎖與共用 Data Protection key store。
- 若需要資料還原能力，再為專案 database 建立獨立 MariaDB 備份與還原驗證。
- 若發生財務爭議，再將可編輯款項改成不可變流水帳與沖銷紀錄。
- 若多人同時編輯造成實際問題，再加入 optimistic concurrency version。
- Pomelo 提供受支援的 EF Core 10 版本且遷移有明確收益時，再依 `docs/EF_CORE_MIGRATION.md` 切換。
- 有量測證據顯示清單查詢不足時，再調整索引、分頁或加入快取。
