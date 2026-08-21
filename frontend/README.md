# 吃土小幫手前端

```sh
pnpm install
cp .env.example .env.local
pnpm dev
```

`VITE_API_BASE_URL` 是 API 的完整 base URL，例如 `http://localhost:5000` 或
`https://chitu-api.konnokai.me`。`VITE_DISCORD_INVITE_URL` 是 scope 為 `bot`
與 `applications.commands`、permission 為 `0` 的公開安裝連結。未設定 API 或 API 未啟動時，畫面會顯示請求失敗，不會使用假資料。

## API 契約假設

- API 回傳的 Discord UID、訂單 ID、款項 ID 與所有金額都是 JSON string；數量是 JSON number。
- `GET /api/orders?role=buyer|requester&archived=true|false` 回傳 `{ orders, summary }`。
- 訂單明細回傳 `{ order, paymentEntries, activities }`，並在 `order.permissions` 提供 API 已判定的操作權限。
- `GET /api/auth/csrf` 回傳 `{ token }`；所有修改請求均帶 `X-CSRF-Token`。
- 寫入端點的 payload 與 `src/types/api.ts` 定義一致。收款模式為 `auto`、`force_completed`、`force_pending`。

前端不計算訂單、收款或差額摘要，只顯示 API 回傳的金額字串。
