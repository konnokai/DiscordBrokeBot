# EF Core 遷移注意事項

目前 schema 由 DbUp 建立，SQL 集中在 `src/DiscordBrokeBot/Migrations`，Store 使用 Dapper。若未來 Pomelo 提供受支援的 EF Core 10 版本且切換有明確收益，先以現有 schema 建立 baseline，再替換 Store 內部，不修改 Discord、API 與 Vue 契約。

## 必須保留的 mapping

- Discord UID 使用 `VARCHAR(32)`，程式碼以 `string` 傳遞，不轉成 JavaScript number。
- 訂單與款項 ID 使用 MariaDB `BIGINT UNSIGNED`，API 以十進位字串回傳。
- 單價、款項與總額使用沒有小數的有正負號 `BIGINT`；單價與數量仍必須大於零，款項不可為零。
- `settlement_override` 的 `NULL` 代表自動判定；`force_completed` 與 `force_pending` 是人工覆寫。
- 所有時間以 UTC 寫入，API 以 ISO 8601 UTC 字串回傳。
- `archived_at IS NULL` 是有效訂單條件。封存訂單不能修改一般欄位、購買狀態或款項。
- 一般欄位使用欄位式更新，不以整列回寫覆蓋另一位使用者的變更。

## 必須保留的交易行為

- 所有更新與刪除 SQL 都要把操作者 UID 與有效狀態放在同一個條件中；受影響筆數為零時不得洩漏資料。
- 款項新增、編輯與刪除先對所屬訂單執行 `SELECT ... FOR UPDATE`，再計算收款總額與收款完成狀態。
- 請求方封存或復原時，在同一個交易中確認沒有款項；代購方可以在有款項時操作。
- 建立訂單時，封鎖檢查與 insert 必須在同一個 transaction 內完成。
- 聚合總額、已收款總額、差額與有效收款狀態由後端計算，前端不得自行推算摘要。

## 驗證

切換後至少重跑單元、MariaDB 整合、API、Discord 互動與前端契約測試，特別確認 UID、負數款項、UTC、封存條件與 row lock 行為沒有改變。
