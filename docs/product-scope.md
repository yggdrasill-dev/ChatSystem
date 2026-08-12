# ChatSystem 產品範疇（Product Scope）

狀態：討論中 draft

定位：個人學習/展示用專案。目標是把一套「房間制聊天室」重新設計成一套可以有效擴展到大量使用者的架構，架構本身的練習是這次 rebuild 的主要目的。

## 1. 這是什麼

一個房間制聊天室 Demo/學習專案。使用者用 Google 帳號登入，進入房間列表選擇建立或加入房間（可設定密碼），進入房間後即時聊天，並保留歷史訊息記錄；另外提供房間後台管理功能。

沿用 `main` 分支既有 Demo 的核心互動流程（登入 → 房間列表 → 建立/加入房間 → 聊天），但這次重建有意做的改變：

- 身分驗證改為 Google OAuth，取代 `main` 上自建帳密註冊的 `AuthServer`
- `webClient` 前端重做（仍然是 Angular），不沿用既有的 Angular 專案內容；另外新增一個 `WebBff` 專案當它的後端，見第 5 節
- 新增訊息歷史記錄（chat history persistence）——`main` 上沒有這個功能
- 新增房間後台管理功能——`main` 上沒有這個功能

## 2. 核心功能範疇

- **登入**：Google OAuth 登入，不自建帳號密碼系統
- **房間**：建立房間（可選密碼）、加入既有房間、房間列表
- **聊天**：房間內即時訊息收發
- **訊息記錄**：房間內歷史訊息需要被保存與查詢（保留多久、怎麼查，待後續設計）
- **房間後台**：房間的管理能力（實際要管理什麼項目，待後續設計時再展開）

## 3. 明確排除（暫定，待確認）

以下是根據目前討論內容推測「這次應該不打算做」的項目，不是已經拍板的決定：

- 好友系統／私訊（1-on-1 聊天）
- 多裝置同步／離線推播
- 金流／付費機制
- Google 以外的第三方登入（Facebook、Apple...等）

## 4. 規模目標

目標方向是「可以有效擴展到大量使用者」，但目前沒有具體數字（例如目標同時在線連線數、房間數量級、單房間訊息吞吐量）。

連線層（Gateway/Dispatcher）已經照可水平擴展的方向設計——多 Gateway 節點、`ConnectionDirectory`（Redis）做跨節點連線定址、Dispatcher 無狀態可任意增減複本。**`CommandRouter` 也已經在 AppHost 開了兩個複本**（2026-08-12），所以「Gateway 與 CommandRouter 都多複本」這個拓樸現在是每次跑 E2E 都會經過的路徑，不是紙上的設計。但再往上（訊息記錄要用什麼儲存、房間人數上限、房間後台查詢效能）這類決策，沒有具體規模數字就只能先抓保守預設值，之後再依實測調整——跟 [connection-layer.md](architecture/connection-layer.md) 第 9 節那兩個「未經驗證的暫定值」是同一種做法。

**Redis 的實體數量刻意不當成架構決定。** 各層只認邏輯名稱（DI 的 service key），AppHost 才把名稱綁到實體資源，現在四個名稱都指向同一顆：`connection-directory`、`room-store`、`identity-store`、`chat-ratelimit`。沒有規模數字的情況下拆成多顆是在猜，而這個做法讓「什麼時候拆、怎麼拆」可以等到有實測需求時再回答——判準是 instance 級的設定需不需要分歧（`maxmemory-policy`、persistence、慢指令的故障範圍），不是分層。詳見 `ChatSystem.AppHost/AppHost.cs` 的註解與 [chat-layer.md](architecture/chat-layer.md) 第 9 節。

## 5. 分層對照

| 層 | 涵蓋範圍 | 狀態 |
|---|---|---|
| 連線層（Gateway/Dispatcher） | 一條 WebSocket 連線怎麼被持有、定址、投遞 | 已完成核心設計與實作，收尾中 |
| 身分/使用者管理層 | Google OAuth 登入、Session、ConnectionId 對應使用者身分、重複登入 Supersede 規則 | **已實作**（含登入/登出 endpoint），只剩 Google client id 這個外部前置作業，見 [identity-layer.md](architecture/identity-layer.md) |
| 房間層 | 建立/加入房間、密碼房、房間成員管理、房間後台 | **已實作**，房間與封鎖名單已在 PostgreSQL、成員名單留 Redis，見 [room-layer.md](architecture/room-layer.md) |
| 聊天層 | 訊息收發、訊息歷史記錄 | **已實作**（階段 A、B 都完成），見 [chat-layer.md](architecture/chat-layer.md) §11。訊息在 PostgreSQL（EF Core）、限流在 Redis 且跨複本 |
| WebBff | 前端的 BFF：出前端靜態檔 + 登入/登出 endpoint（見 [identity-layer.md](architecture/identity-layer.md) ADR-10） | 已實作 |
| webClient | Angular 前端，重做（沿用 `main` 的資料夾名） | 待實作 |

## 6. 待確認事項

- 規模目標沒有具體數字，會影響後續每一層的儲存/擴展決策
- ~~房間後台管理功能具體要管理什麼~~ 已確認四項：踢出成員、封鎖使用者、關閉／刪除房間、修改房間設定（見 [room-layer.md](architecture/room-layer.md)）。「監看訊息」不在其中
- ~~訊息記錄要保留多久、要不要分頁查詢~~ 已決定：保留 **90 天**（暫定值，未經驗證）、keyset 分頁，見 [chat-layer.md](architecture/chat-layer.md) ADR-6 與 5.2。**搜尋仍然沒做**——它的查詢成本跟分頁不同量級，會推翻該文件 ADR-5「歷史查詢走 WebSocket」的判斷
- **使用者的顯示名稱**：房間成員名單目前只有 Google `sub`（一串數字），UI 上不能看。登入時已經把 Google 的 `name`／`picture` 存下來（[identity-layer.md](architecture/identity-layer.md) ADR-11）。**部分解決**：聊天訊息會內嵌送出當下的名稱快照（[chat-layer.md](architecture/chat-layer.md) ADR-3），所以聊天視窗不需要查詢介面；**成員名單仍然需要批次查詢介面**，那要等 webClient 才會被逼出來。「暱稱可不可以自己改」還是產品決定，但快照語意讓它變得無害——改名不會改寫歷史訊息
- ~~**關閉的房間，歷史訊息還能不能看？**~~ **已決定：不能看，因為訊息會被刪掉。** 關閉房間＝**刪除**房間，該房的歷史訊息與封鎖名單一起刪（[chat-layer.md](architecture/chat-layer.md) ADR-10）。理由是這是個 demo 專案，歷史訊息沒有長期保留的價值；連帶結果是 `IsClosed` 這個狀態整個消失（它唯一的用途就是不讓歷史訊息變孤兒），房間只有「在」與「不在」。**兩個後果要記著**：刪除不可逆、沒有垃圾桶，所以 webClient 的關閉房間必須二次確認；而實作**已完成**（`IsClosed` 已經從系統裡消失，但儲存仍是 Redis——換 PostgreSQL 是分開的下一步）
- ~~房間本身與封鎖名單需要持久儲存，這跟訊息記錄是**同一個儲存決定**，建議一起做，不要為房間層單獨選一個~~ **已決定：PostgreSQL**（[chat-layer.md](architecture/chat-layer.md) ADR-4），`rooms` / `room_bans` / `messages` 三張表同一個資料庫，理由就是本條原本寫的那個：同一套 migration／備份策略。Session / Presence / 成員名單**不遷**，維持 Redis（TTL 與 compare-and-swap 語意放進關聯式資料庫會變難看且變慢）
- 第 3 節的排除清單只是根據目前對話推測，需要你確認是否有遺漏（該做但沒提到／不該做但被列進來）

已確認的產品層面決定（原本沒有記在任何地方）：

- 一個使用者**一次只能在一間房**。加入新房間會自動離開舊房間
- WebSocket 斷線重連（網路抖動、換分頁、重複登入被 Supersede）時，房間裡其他成員**什麼都看不到**——成員資格保留一段寬限期，設計見 [room-layer.md](architecture/room-layer.md) ADR-2
- **未登入的使用者連不上 WebSocket**：身分驗證在 handshake 就完成（[identity-layer.md](architecture/identity-layer.md) ADR-8），沒有「先連上、之後再登入」這種狀態。前端必須先完成 Google 登入拿到 session cookie，才有辦法建立連線
- **登入服務與 WebSocket 入口必須同源部署**（同一個網域，正式環境靠同一個 ingress 分路徑）。這是 cookie 認證的技術約束，但會影響部署拓樸的選擇，見 [identity-layer.md](architecture/identity-layer.md) ADR-10
- **同一帳號不能同時開兩個分頁**：後開的連線會把先開的踢斷（Supersede，[identity-layer.md](architecture/identity-layer.md) ADR-4）。目前被踢的分頁只會看到連線斷掉、不知道原因，如果它自動重連就會反過來踢掉新分頁——要不要讓 client 收到明確的原因還沒決定（見該文件第 9 節）
