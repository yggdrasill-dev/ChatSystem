# 聊天層架構設計（Chat Layer）

狀態：**設計中**，尚未實作
技術棧：延續 .NET 10 + NATS（Adaptare）+ protobuf；**新增 PostgreSQL 作為正式持久儲存**（見 ADR-4）
範圍：**訊息本身——收發、持久化、歷史查詢**，不含「誰該收到」（房間層）與「怎麼投遞」（連線層）
依賴：命令的解析與分派由協定層負責（[protocol-layer.md](protocol-layer.md)）；成員名單與 fan-out 名單向房間層取得（[room-layer.md](room-layer.md)）；送出者的顯示名稱向身分層取得（[identity-layer.md](identity-layer.md)）

> 這是最後一個未設計的核心層，也是唯一一個**逼出正式持久儲存決定**的層。`room-layer.md` ADR-7 把房間與封鎖名單的儲存標為 provisional 並明說「跟聊天層一起決定」，`product-scope.md` §6 也寫「這是同一個儲存決定，不要為房間層單獨選一個」——本文件 ADR-4 就是那個決定。

> **本文件初稿的核心設計已被推翻。** 初稿讓 PostgreSQL 交易擁有「房間內訊息全序」這個領域不變量（`UPDATE rooms SET last_seq = last_seq + 1 RETURNING`），結果是整層變成以資料庫操作為中心：業務規則藏在 SQL 的 WHERE 子句、`IChatMessageStore` 表面上不綁儲存技術實際上綁死了「儲存必須有交易」、`rooms` 表成為每則訊息一次 UPDATE 的熱點。推翻的完整過程記在 ADR-2，因為**錯的方式比結論有價值**。

## 1. 範圍界定

這層要解決的問題：

1. **訊息收發**：把一則訊息廣播給房間內所有在線成員。
2. **訊息記錄**：訊息的持久化、房間內的排序、歷史分頁查詢。
3. **保留期限**：訊息存多久、過期怎麼清。
4. **本系統的正式持久儲存選型**，並把房間層 provisional 的部分一起接手。

明確排除：誰該收到（房間層的 `IRoomMembership`）、怎麼投遞（連線層的 `IOutboundGateway` → `Dispatcher`）、身分怎麼證明（身分層的 handshake 驗證）。

**與房間層的邊界**（延續 `room-layer.md` §1 定下的分工，本文件不改動它）：房間層回答「誰該收到」，聊天層回答「內容是什麼、要不要存」。聊天層拿到一則訊息時向房間層取得成員名單，再自己呼叫 `IPacketPublisher`。這條邊界的具體體現是 `RoomBroadcaster`——它已經把「userId 名單 → 解析成 connectionId → 投遞」封裝好了，聊天層直接重用（見 §6.8）。

## 2. 現有實作對照（`main` 分支）

`main` 的對應物是 `ChatServer`，核心是 `ChatServer/Models/Endpoints/ChatSendHandler.cs`。四個差異：

- **`main` 完全沒有持久化**。訊息是純 fan-out，送完就消失——`product-scope.md` §1 把「訊息歷史記錄」列為本次**新增**的功能，本層一半以上的複雜度來自這一點。
- **`main` 的 fan-out 自己做分組並直接 publish 到 `connect.send.{connectorId}`**，繞過了現在的 `Dispatcher`。這次一律走 `IPacketPublisher` → `IOutboundGateway` → `dispatch.deliver`（`protocol-layer.md` ADR-5：下行只有一條路徑）。
- **`main` 的 `ChatMessage` 內建 `Scope.Person`（私訊）與 `Scope.System`**。`product-scope.md` §3 已把私訊排除，所以本層的訊息型別沒有 `Scope` 欄位（見 §8）。
- **`main` 用 `PlayerInfoQuery` 依 `sessionId` 反查送出者**，再用 `GetRoomBySessionidQuery` 反查他在哪間房——每則訊息兩次反查。這次前者由 `context.Principal` 直接給（`protocol-layer.md` ADR-9），後者仍需要一次查詢，但它同時就是授權檢查（見 ADR-5）。

值得保留的一點：`main` 的 `sendContent.From = fromPlayerInfo.Name` 送的是**名字而不是 id**。本層 ADR-3 做同樣的事，但多做一步——把那個名字**存進訊息記錄**，而不只是放進當下那一則廣播。

## 3. 核心概念

| 概念 | 職責 |
|---|---|
| `ChatMessageDraft` | **未定序**的訊息：房間、送出者、顯示名稱、內容。內容的有效性由它自己定義（見 6.1） |
| `ChatMessage` | **已定序**的訊息：draft 加上 `OrderKey` 與 `SentAt`。從 draft 到它的轉換就是「發號」這件事 |
| `OrderKey` | 房間內用來排序與分頁的 `int64`。**單調但稀疏**——不保證連續，不能拿來計數或偵測缺口（見 ADR-2） |
| `IMessageSequencer`（新） | 發號的接縫。目前是無狀態的微秒時鐘實作；房間 actor 若落地，換掉的是註冊那一行（見 ADR-9） |
| `IChatMessageStore`（新） | **append-only**：一次 `INSERT`、一個 keyset 分頁查詢、一個批次刪除。沒有 read-modify-write，不要求交易 |
| `ChatRetention`（新） | 保留期限的政策值，`ChatRetentionSweeper` 用它 |
| `chat.send` / `chat.history` | 本層向 `CommandRouter` 註冊的兩個命令。**兩個都不帶 `room_id`**（見 ADR-5） |

## 4. 元件關係圖

```mermaid
graph TB
    subgraph "CommandRouter（協定層宿主）"
        SH["ChatSendHandler"]
        HH["ChatHistoryHandler"]
        SW["ChatRetentionSweeper\n(BackgroundService)"]
    end

    subgraph "聊天層（Common.Chat）"
        SQ["IMessageSequencer\n(無狀態，程序內)"]
        MS[("IChatMessageStore\nappend-only")]
    end

    subgraph "房間層"
        RM[("IRoomMembership\nRedis：誰在這間房")]
        RB["RoomBroadcaster\n(userId → connectionId → 投遞)"]
    end

    subgraph "身分層"
        UP[("IUserProfileStore\nRedis：display_name")]
    end

    subgraph "連線層"
        DP[Dispatcher]
        GW[Gateway 節點]
    end

    SH -- "1. 你在哪間房（同時是授權）" --> RM
    SH -- "2. 我叫什麼名字" --> UP
    SH -- "3. 發號" --> SQ
    SH -- "4. 單一 INSERT（先存後廣播，ADR-1）" --> MS
    SH -- "5. 廣播" --> RB
    HH -- "授權：你在這間房嗎" --> RM
    HH -- "keyset 分頁" --> MS
    HH -- "回覆發問的連線" --> RB
    SW -- "刪除過期訊息" --> MS
    RB --> DP
    DP --> GW
```

**沒有任何箭頭從寫入路徑指向房間資料。** 這是初稿與現在最大的差別——初稿的寫入要 `UPDATE rooms`，所以 `rooms` 表在圖上是寫入路徑的一部分。現在 `messages` 的寫入完全不碰它。

## 5. 訊息序列

### 5.1 送出一則訊息

```mermaid
sequenceDiagram
    participant WC as WebClient
    participant CR as CommandRouter
    participant H as ChatSendHandler
    participant RM as IRoomMembership
    participant UP as IUserProfileStore
    participant SQ as IMessageSequencer
    participant MS as IChatMessageStore
    participant RB as RoomBroadcaster

    WC->>CR: chat.send { body }
    Note over WC,CR: 刻意沒有 room_id——伺服器自己知道你在哪間房（ADR-5）
    CR->>H: HandleAsync(CommandContext{connectionId, principal}, SendChatMessageRequest)
    H->>H: ChatMessageDraft.Validate(body)（空／超長 → 回覆並結束）
    H->>RM: GetCurrentRoomAsync(userId)
    RM-->>H: roomId（null → NOT_IN_ROOM，回覆並結束）
    Note over H,RM: 這一步同時是授權、定址、以及「房間還開著嗎」<br/>——關房會清空成員名單，所以三件事一次查詢（ADR-8）
    H->>H: 限流檢查（超過 → RATE_LIMITED，回覆並結束，ADR-7）
    H->>UP: GetDisplayNameAsync(userId)
    H->>SQ: Next()
    SQ-->>H: orderKey（微秒時鐘 + 原子單調守衛，ADR-2）
    H->>MS: AppendAsync(ChatMessage)
    Note over MS: 單一 INSERT。沒有交易、沒有 UPDATE、不碰 rooms 表
    H->>RM: GetMembersAsync(roomId)
    H->>RB: ToUsersAsync(成員, ChatMessage)
    Note over H,RB: 寫入成功之後才廣播。順序不能反過來（ADR-1）
```

### 5.2 歷史分頁查詢

```mermaid
sequenceDiagram
    participant WC as WebClient
    participant H as ChatHistoryHandler
    participant RM as IRoomMembership
    participant MS as IChatMessageStore
    participant RB as RoomBroadcaster

    WC->>H: chat.history { before_order_key, limit }
    H->>RM: GetCurrentRoomAsync(userId)
    RM-->>H: roomId（null → NOT_IN_ROOM）
    H->>MS: GetPageAsync(roomId, beforeOrderKey, limit)
    Note over MS: WHERE room_id = $1 AND order_key < $2 ORDER BY order_key DESC LIMIT $3<br/>直接走主鍵 (room_id, order_key)，不需要額外索引
    MS-->>H: 一頁訊息（order_key 由大到小）+ has_more
    H->>RB: ReplyAsync(context, ChatHistory)
    Note over H,RB: 回覆直接送回 context.ConnectionId，不查 Presence<br/>（沿用 RoomBroadcaster 既有的理由：Supersede 之後 Presence 可能指向別條連線）
```

`before_order_key = 0` 代表「從最新的開始」。往上滑載入更舊的訊息時，client 把上一頁最小的 `order_key` 當成下一次的游標——這就是 keyset 分頁，不用 OFFSET，所以翻到第 100 頁跟第 1 頁一樣快，**而且完全不受排序鍵稀疏的影響**（見 ADR-2）。

### 5.3 重連後補齊

```mermaid
sequenceDiagram
    participant WC as WebClient
    participant RJ as RoomJoinHandler
    participant CH as ChatHistoryHandler

    Note over WC: 網路抖動 → 重連 → 重新送 room.join
    WC->>RJ: room.join { roomId }
    RJ-->>WC: RoomJoined（房間層 ADR-2：其他成員全程無感）
    WC->>CH: chat.history { before_order_key: 0, limit: 50 }
    CH-->>WC: 最新 50 則
    Note over WC: 跟本地最後一則的 order_key 比對，比它大的就是斷線期間的訊息
```

這條路徑是 `room-layer.md` ADR-2 那句「寬限期內送出的訊息**不補送**，使用者重連後靠聊天層的歷史記錄補齊」的兌現。那條 ADR 是在聊天層還不存在時寫的，等於預先開了一張支票——本節就是它。

**注意 client 這裡做的是「比對游標」而不是「偵測缺口」。** 前者只需要單調，後者需要連續，而 `order_key` 只保證前者。這是刻意的差別，見 ADR-2。

## 6. 具體介面設計

### 6.1 領域模型（`Common.Chat`）

初稿沒有這一節，那是它最根本的問題：整層只有 handler、store、資料表，`ChatMessageRecord` 是一列資料表穿了 C# record 的衣服。對照房間層有 `Room`、`RoomMember`、`RoomPassword`、`RoomGraceSweeper`——那些是概念。

```csharp
namespace Common.Chat;

public enum ChatMessageRejection
{
	None,
	BodyEmpty,
	BodyTooLong,
}

// 尚未定序的訊息。訊息的有效性是領域的性質，不是 schema 的 NOT NULL、
// 也不是 handler 裡隨手寫的兩個 if——把它放在這裡，規則只有一個地方可以改。
public sealed record ChatMessageDraft(
	string RoomId,
	string SenderUserId,
	string SenderDisplayName,
	string Body)
{
	public const int MaxBodyLength = 4096;   // 暫定值，見 §9

	public static ChatMessageRejection Validate(string body) => body switch
	{
		_ when string.IsNullOrWhiteSpace(body) => ChatMessageRejection.BodyEmpty,
		_ when body.Length > MaxBodyLength => ChatMessageRejection.BodyTooLong,
		_ => ChatMessageRejection.None
	};

	public ChatMessage WithOrder(long orderKey, DateTimeOffset sentAt) =>
		new(RoomId, orderKey, SenderUserId, SenderDisplayName, Body, sentAt);
}

// 已定序的訊息。從 draft 到這裡的那一步就是「發號」，而發號的擁有者
// 是可以換的（現在是程序內的時鐘，未來可能是房間 actor，見 ADR-9）——
// 用兩個型別把那一步標示出來，換擁有者時就知道要動哪裡。
public sealed record ChatMessage(
	string RoomId,
	long OrderKey,
	string SenderUserId,
	string SenderDisplayName,
	string Body,
	DateTimeOffset SentAt);

public sealed record ChatMessagePage(
	IReadOnlyList<ChatMessage> Messages,   // order_key 由大到小
	bool HasMore);
```

**刻意不發明更多名詞。** 「這個人現在能不能發言」是四個檢查的組合（內容合法、在房間裡、沒被限流），把它包成一個 `SpeakingRight` 之類的型別很誘人，但房間層的 handler 就是一連串有守衛的提前返回（`RoomJoinHandler` 有四個），本層跟著做才一致。初稿的問題不是「檢查沒有被包成型別」，是**其中一個檢查被藏進了 SQL 的 WHERE 子句**——那個問題由 ADR-8 解決，不需要新型別。

### 6.2 Proto（`Common/Protos/chat.proto`，新檔案）

```protobuf
syntax = "proto3";

option csharp_namespace = "Chat.Protos";
package chat;

// 聊天層自己的命令與下行訊息。協定層不認識這些型別，對應表由 AddChatPackets() 註冊。

// ---- 上行 ----

// 刻意沒有 room_id：使用者一次只能在一間房，「送到哪間房」是伺服器知道的事實，
// 不是 client 說了算（ADR-5）。
message SendChatMessageRequest {
	string body = 1;
}

message ChatHistoryRequest {
	// 0 = 從最新的開始；否則回傳 order_key 嚴格小於它的那一頁（keyset 分頁）。
	int64 before_order_key = 1;
	// 0 或超過上限時由伺服器夾到預設值，不回錯誤。
	int32 limit = 2;
}

// ---- 下行 ----

message ChatMessage {
	string room_id = 1;

	// 房間內單調遞增的排序鍵與分頁游標。
	//
	// **稀疏，不保證連續。** 不要拿它計數（則數 != max - min），也不要拿它偵測
	// 缺口（expected = last + 1 一定會誤判）。要判斷「有沒有新訊息」請比大小，
	// 不要做算術。命名刻意不叫 seq，就是為了不誘發那種寫法（ADR-2）。
	int64 order_key = 2;

	string sender_user_id = 3;
	// 送出當下的顯示名稱快照，不是即時查詢的結果（ADR-3）。
	string sender_display_name = 4;
	string body = 5;

	// 實際發生時間，跟 order_key 是兩件事：order_key 在突發時可能借用未來的
	// 微秒，sent_at 不會（ADR-2）。顯示時間一律用這個欄位。
	int64 sent_at_unix_ms = 6;
}

message ChatHistory {
	string room_id = 1;
	// order_key 由大到小（最新的在前）。
	repeated ChatMessage messages = 2;
	bool has_more = 3;
}

// 沿用 RoomOperationReply 的形狀。業務失敗一定要用明確的下行訊息回覆
// （protocol-layer.md ADR-8）。
message ChatOperationReply {
	enum Status {
		OK = 0;
		// 不在任何房間。房間被關掉也走這一條——關房會清空成員名單，
		// 所以兩件事在這裡是同一個結果（ADR-8）。
		NOT_IN_ROOM = 1;
		BODY_EMPTY = 2;
		BODY_TOO_LONG = 3;
		RATE_LIMITED = 4;
	}

	Status status = 1;
	string room_id = 2;
}
```

**`OK = 0` 在這裡是安全的**，但理由要寫清楚，因為 `protocol-layer.md` §9 有一條相反方向的教訓（`InboundAck.OK` 原本是 0，序列化成 0 bytes，經過 NATS 回來變 `null`，每個成功的命令都殺掉連線）。差別在於那條教訓只適用於 **request/reply 的 reply**——「0 bytes 的回覆」跟「沒有回覆」在那裡無法區分。`ChatOperationReply` 走的是一般下行投遞（`IPacketPublisher`），沒有「沒有回覆」這個對立面；而且成功路徑上 `room_id` 永遠非空，本來就不會序列化成 0 bytes。與 `RoomOperationReply` 形狀一致比自作聰明重要。

**沒有 `ROOM_CLOSED`**：初稿有，因為初稿的 `UPDATE ... WHERE NOT is_closed` 分得出那兩種情況。改成 append-only 之後不再多讀一次房間，而關房本來就會清空成員名單，所以「房間關了」對送訊息的人來說就是「你不在任何房間」。少一個狀態、少一次 Redis 讀取——**拿掉資料庫中心的設計讓這一層變小了，不是變大**。

### 6.3 `IChatMessageStore`（`Common.Chat`）

```csharp
namespace Common.Chat;

public interface IChatMessageStore
{
	// 單一 INSERT。訊息在進來之前就已經定序完成（OrderKey 由 IMessageSequencer 給），
	// 所以這裡沒有 read-modify-write，也不需要交易。
	//
	// 主鍵衝突（同一房間、同一 order_key）回 false 而不是丟例外——呼叫端重新發號
	// 再試一次即可，理由見 6.4。
	ValueTask<bool> TryAppendAsync(ChatMessage message, CancellationToken cancellationToken = default);

	// beforeOrderKey = 0 代表從最新的開始。
	ValueTask<ChatMessagePage> GetPageAsync(
		string roomId,
		long beforeOrderKey,
		int limit,
		CancellationToken cancellationToken = default);

	// 回傳實際刪除的筆數，讓 sweeper 知道要不要再跑一輪（見 ADR-6）。
	ValueTask<int> DeleteOlderThanAsync(
		DateTimeOffset cutoff,
		int batchSize,
		CancellationToken cancellationToken = default);
}
```

**這個介面現在真的不綁儲存技術**——三個方法分別是 append、range scan、range delete，任何 key-value 或文件儲存都給得起。初稿宣稱同一件事（ADR-4 寫「介面刻意不綁任何儲存技術」）但那是假的：它要求交易內的 read-modify-write，等於綁死了關聯式資料庫。**這是本次重寫最實質的改變**，也讓 ADR-4「換掉的成本是一次資料遷移」從願望變成事實。

### 6.4 `IMessageSequencer`（`Common.Chat`）

```csharp
namespace Common.Chat;

// 發號的接縫。今天是程序內的時鐘，明天可能是房間 actor（ADR-9）——
// 把它獨立成介面，換擁有者時動的是 DI 註冊那一行。
public interface IMessageSequencer
{
	long Next();
}

// 微秒時鐘 + 單調守衛。
internal sealed class MonotonicMicrosecondSequencer(TimeProvider timeProvider) : IMessageSequencer
{
	private long m_Last;

	public long Next()
	{
		var observed = timeProvider.GetUtcNow().ToUnixTimeMilliseconds() * 1000L;

		// 這個 CAS 迴圈是必要的，不是防禦性寫法。一個 CommandRouter process 裡有多個
		// handler 併發，非原子的 `m_Last = Math.Max(observed, m_Last + 1)` 會讓兩個
		// 執行緒讀到同一個 prior 而發出同一個號——那會把「主鍵衝突」從幾乎不可能
		// 變成經常發生。
		long prior, next;

		do
		{
			prior = Volatile.Read(ref m_Last);
			next = Math.Max(observed, prior + 1);
		}
		while (Interlocked.CompareExchange(ref m_Last, next, prior) != prior);

		return next;
	}
}
```

三個要說清楚的性質：

- **突發時會借用未來的微秒。** `Math.Max(observed, prior + 1)` 讓同一微秒內到達的訊息一路往後排。偏移只在**單一 process** 的到達率超過每微秒一則（＝100 萬則/秒）時才會累積，而每則訊息要走七次網路往返，實務上到不了；而且真實時鐘一追上就會重新採用時鐘值，**偏移不跨突發累積**。這也正是 `OrderKey` 與 `SentAt` **必須是兩個欄位**的理由——借用未來的是排序鍵，顯示時間不受影響。
- **NTP 往回大跳時，排序鍵與時間戳會短暫不一致。** 若時鐘被往回修正 5 秒，守衛會讓 `OrderKey` 純靠遞增撐過那 5 秒（仍然單調、仍然唯一），但同一批訊息的 `SentAt` 會往回跳，於是「列表順序」跟「顯示時間」在那個窗口內對不上。罕見、會自癒、只是觀感問題，記在這裡而不是假裝沒有。
- **跨 process 的碰撞靠重試，不靠協調。** 兩個複本要撞上同一個 `order_key` 需要同一間房、同一微秒、兩個 process 同時寫入。房間的訊息速率是人類尺度的，實務上不會發生；真的發生就是 `TryAppendAsync` 回 `false`，重新發號再試一次。因為寫入是單一 INSERT、沒有交易，重試就是重發一次 statement，沒有回滾要處理。

### 6.5 PostgreSQL 實作

儲存是 `IChatMessageStore` 與房間層兩個 store 的**實作細節**，不是本層的設計中心。這一節的地位相當於 `room-layer.md` 6.1 底下那張 Redis key 設計表。

```sql
CREATE TABLE rooms (
    room_id       text        PRIMARY KEY,
    name          text        NOT NULL,
    password_hash text        NULL,          -- NULL = 公開房
    owner_user_id text        NOT NULL,
    created_at    timestamptz NOT NULL,
    is_closed     boolean     NOT NULL DEFAULT false
);
-- 沒有 last_seq。排序鍵由應用產生，rooms 完全不在訊息的寫入路徑上（ADR-2）。

CREATE TABLE room_bans (
    room_id   text        NOT NULL REFERENCES rooms(room_id),
    user_id   text        NOT NULL,
    banned_at timestamptz NOT NULL,
    PRIMARY KEY (room_id, user_id)
);

CREATE TABLE messages (
    room_id             text        NOT NULL REFERENCES rooms(room_id),
    order_key           bigint      NOT NULL,
    sender_user_id      text        NOT NULL,
    sender_display_name text        NOT NULL,
    body                text        NOT NULL,
    sent_at             timestamptz NOT NULL,
    PRIMARY KEY (room_id, order_key)
);

-- 只給保留期限的清理用（ADR-6）。查詢路徑不需要它。
CREATE INDEX messages_sent_at_idx ON messages (sent_at);
```

- **主鍵 `(room_id, order_key)` 直接服務唯一的讀取路徑**，不需要任何額外索引。**排序鍵稀疏對 B-tree 完全沒有影響**——`bigint` 不管值多大都是 8 bytes，索引不在乎密度，keyset 分頁在稀疏鍵上行為跟連續鍵完全一樣。稀疏唯一殺掉的是「對鍵做算術」，而那正是 ADR-2 明確放棄的東西。
- 主鍵同時是**跨 process 碰撞的守門**（6.4）：撞到就是 `23505`，`TryAppendAsync` 回 `false`。
- **`room_bans` 用複合主鍵而不是 surrogate id**。房間層的 `IRoomBanList` 三個方法分別對應 `SELECT` / `INSERT ... ON CONFLICT DO NOTHING` / `DELETE`，全部靠主鍵，跟 Redis Set 版本的原子性語意一樣，所以 `room-layer.md` 6.1 那句「`IRoomBanList` 因此不需要 Try 語意」遷移後仍然成立。

### 6.6 遷移與 schema 管理

用 **Npgsql + Dapper 手寫 SQL + 冪等的啟動時 migration**，不用 EF Core：

- 本層的查詢只有四種形狀（append、keyset 分頁、批次刪除、房間層那幾個 CRUD），全部是手寫得出來的 SQL。EF Core 的價值在複雜查詢的組合與變更追蹤，這裡兩者都用不到，卻要付 DbContext 生命週期、追蹤器、以及「產生的 SQL 跟你想的不一樣」的成本。
- 既有的每個 store 都是「介面 + 手寫指令」的形狀（`RedisRoomStore`、`RedisSessionStore`…），維持一致。
- **代價**：schema 變更要自己管。啟動時跑一次 `CREATE TABLE IF NOT EXISTS` 對 demo 夠用，但**它不處理欄位變更**——真的要改欄位時得引入 migration 工具（DbUp／FluentMigrator）或手動處理。這個代價現在就寫下來，免得日後把它當成「疏漏」而不是「當時的取捨」。

### 6.7 命令註冊

```csharp
// Common/ChatLayerServiceCollectionExtensions.cs
public static IServiceCollection AddChatStore(this IServiceCollection services)
{
	services.TryAddSingleton(TimeProvider.System);
	services.AddSingleton<IMessageSequencer, MonotonicMicrosecondSequencer>();
	return services.AddSingleton<IChatMessageStore, PostgresChatMessageStore>();
}

public static IServiceCollection AddChatPackets(this IServiceCollection services)
{
	services.AddPacketHandler<SendChatMessageRequest, ChatSendHandler>("chat.send");
	services.AddPacketHandler<ChatHistoryRequest, ChatHistoryHandler>("chat.history");

	services.AddOutboundPacket<ChatMessage>("chat.message");
	services.AddOutboundPacket<ChatHistory>("chat.history.reply");
	return services.AddOutboundPacket<ChatOperationReply>("chat.reply");
}

// 保留期限的清理（ADR-6）。跟 AddRoomMembershipMaintenance() 是同一種形狀。
public static IServiceCollection AddChatRetention(this IServiceCollection services) =>
	services.AddHostedService<ChatRetentionSweeper>();
```

`IMessageSequencer` 註冊為 **singleton**：單調守衛的狀態必須是整個 process 共用的，註冊成 scoped 會讓每則命令拿到一個新的守衛，那個 CAS 迴圈就白寫了。

AppHost 新增一個 `chat-db` 資源（`builder.AddPostgres("chat-db").WithDataVolume()`），`CommandRouter` 加一條 `WithReference`。**房間層的 `room-store` Redis 在遷移完成後只剩成員名單**，見 §9。

### 6.8 重用 `RoomBroadcaster`

聊天層的 fan-out 跟房間層完全一樣：拿 `userId` 名單 → `IPresenceDirectory.ResolveConnectionsAsync` → `IPacketPublisher`。`RoomBroadcaster` 已經是這個形狀。

決定：**保持原位、不改可見度、聊天層直接用**（同屬 `Common` 組件）。它的職責就是「把訊息送給一群房間成員」，那對聊天層與房間層是同一件事；為了「聊天層不該依賴 `Common.Rooms`」而複製一份，只會讓兩邊行為漂移——這正是 `room-layer.md` §9 拒絕「建房順便加入」時用過的同一個論證。聊天層本來就依賴房間層（要問成員名單），多用一個 broadcaster 沒有增加耦合方向。

## 7. 架構決策記錄（ADR）

### ADR-1：先存後廣播，不是先廣播後存

- **Context**：先廣播後存的延遲比較低——client 立刻看到訊息，寫入在背景完成。多數聊天系統的直覺選擇。
- **Decision**：**寫入成功之後才廣播**。`TryAppendAsync` 回 `false`（重試後仍失敗）或丟例外時，這則訊息不會被送給任何人。
- **Consequences**：
  - 這不是效能取捨，是被 `room-layer.md` ADR-2 決定的。那條 ADR 寫「寬限期內送出的訊息不補送，使用者重連後靠聊天層的歷史記錄補齊」——**歷史記錄因此是真相來源**。先廣播後存會產生「在線的人看到了、歷史裡沒有」的訊息，重連的人永遠看不到它，那條 ADR 當場變成空的。
  - **代價是實質的**：每則訊息在廣播前多一次 Postgres 往返，而 `protocol-layer.md` ADR-2 的 request/reply 讓那條連線在此期間阻塞（單一連線同時只有一則訊息 in-flight）。叢集內一次 INSERT 是毫秒級，聊天場景可以接受，但這是本層最主要的延遲來源。
  - **這條 ADR 在改成 append-only 之後成本降低了**：初稿是「交易 + UPDATE + INSERT」，現在是單一 INSERT，而且不跟同房的其他訊息搶 `rooms` 那一列的鎖。

### ADR-2：排序鍵由應用產生，單調但稀疏——不是 DB 交易、不是 Redis `INCR`、不是 snowflake

- **Context**：訊息需要一個穩定的排序鍵與分頁游標。**這一條前後被推翻兩次，過程比結論有價值。**
- **Decision**：`int64` 的 `order_key`，由程序內的 **微秒時鐘 + 原子單調守衛** 產生（6.4）。**保證單調與唯一，不保證連續。**

被否決的五個方案，依當初考慮的順序：

- **(a) 純 server 時間戳**：`CommandRouter` 多複本、時鐘不同步，且同微秒會撞主鍵。缺的是單調守衛——最終方案其實就是 (a) 加上那個守衛。
- **(b) Redis `INCR {rooms}:seq:{roomId}`**：**本文件第一版的選擇。** `seq` 的主要價值是讓 client 偵測「我漏訊息了嗎」，而那建立在「有缺口就代表真的漏了」。Redis 發號、Postgres 存訊息是兩個儲存——發號成功但 INSERT 失敗就留下**永久缺口**，client 會誤判並反覆補拉。**分配式序號必然有洞，所以它自己破壞了自己的主要價值。**
- **(c) 全域 `BIGSERIAL`**：給得起排序與游標，但房間內不連續（其他房間的訊息穿插），一樣偵測不到缺口，而且排序權仍在 DB 手上。
- **(d) 交易內遞增 `rooms.last_seq`**：**本文件第二版的選擇，也是這次被推翻的那個。** 它確實拿到了連續無洞（交易 rollback 時計數器一起 rollback），但代價是整層變成以資料庫操作為中心：
  - 領域不變量（房間內全序）被寄放在 Postgres 的 row lock 裡，**沒有 Postgres 就沒有這個保證**；
  - `IChatMessageStore` 表面上不綁儲存技術，實際上要求交易內的 read-modify-write；
  - `rooms` 表成為每則訊息一次 `UPDATE` 的寫入熱點，單一房間的吞吐上限＝一次交易時間的倒數；
  - 而最關鍵的一點是**它擋住了房間 actor**（ADR-9）：actor 落地時，那整套交易邏輯是要丟掉的。
  - 真正的錯不在選了 (d)，在於**發明了「連續」這個需求**：它唯一的消費者是缺口偵測（一個 nice-to-have），卻用整層的架構去換。
- **(e) Snowflake（41 bit 毫秒 + 10 bit 節點 + 12 bit 序號）**：評估過，不選。中間那 10 bits 需要**小而互斥**的節點編號，而 Gateway 現有的 nodeId 是 `Guid.NewGuid()`（`Gateway/Program.cs:35`），壓到 10 bits 會碰撞；要拿到真正互斥的編號就得引進 Redis 租約或 K8s ordinal——**那是協調，而本方案的整個賣點就是不需要協調**。另外一個實際差別：snowflake 的值是 1e17～1e18 量級，遠超過 JS 的 2^53，前端一定要當字串或 `BigInt` 處理；微秒時間戳現在約 1.786e15，**低於 2^53，TypeScript 可以直接當 number 用**（安全到約西元 2255 年）。下一層就是 Angular 的 webClient，這個差別會直接踩到。

- **Consequences**：
  - **儲存變回 dumb sink**：單一 INSERT，沒有交易、沒有 RMW、不碰 `rooms`。`IChatMessageStore` 的抽象因此是誠實的（6.3）。
  - **`rooms` 的寫入熱點消失**，同房訊息不再互相排隊。
  - **放棄缺口偵測。** 在線期間 NATS 丟一則下行訊息，client 不會知道（core NATS 沒有 retry，`protocol-layer.md` §8）。重連補齊不受影響，因為那只需要比較游標大小，不需要連續（5.3）。
  - **不同複本的時鐘偏移可能讓兩位使用者幾乎同時的訊息相對順序風向。** 這在任何無協調方案裡都存在，而且那個順序本來就是模糊的；**同一個人的訊息順序仍然被 `protocol-layer.md` ADR-2 從結構上釘住**（單一連線同時只有一則 in-flight）。
  - **命名是防護的一部分**：欄位叫 `order_key` 而不是 `seq`。一個叫 `seq` 的 `int64` 幾乎保證有人會在前端寫 `expected = last + 1`，而那一定會誤判。proto 的註解直接把禁止事項寫出來（6.2）。
  - **這是唯一不會擋住 ADR-9 的排序方案**：append-only + 應用發號正是 actor 想要的形狀，actor 落地時換掉的是 `IMessageSequencer` 的註冊，`IChatMessageStore` 一行都不用改。

### ADR-3：訊息內嵌送出者的顯示名稱快照

- **Context**：`ChatMessage` 可以只存 `sender_user_id`（Google `sub`，一串數字），讓 client 另外批次查 profile；也可以把送出當下的 `display_name` 一起存進訊息。
- **Decision**：**存快照**。寫入時讀一次 `Profile:{userId}` 的 `display_name` 存進訊息列。
- **Consequences**：
  - 歷史查詢不需要 join、不需要批次查 profile、不需要 client 端的 profile 快取與失效處理。一頁 50 則訊息就是一次查詢。
  - **改名之後舊訊息維持舊名字**。這是刻意的語意而不是缺陷——聊天記錄應該呈現「他當時叫什麼」，這也是 `main` 分支 `ChatSendHandler` 已經在做的事（`sendContent.From = fromPlayerInfo.Name`），只是 `main` 沒有存下來。
  - **代價：熱路徑上多一次 Redis `HGET`**。每則訊息一次，跟房間層每個 handler 的 Redis 往返同量級，不優化。想消掉它就得把 display name 塞進 `CommandContext`，但 `protocol-layer.md` ADR-9 明說 principal 是不透明字串、協定層「不知道它的值代表什麼」——為了省一次 Redis 讀取而打破那個不透明性不划算。
  - **只回答了 `identity-layer.md` ADR-11 的一半**。那條 ADR 說查詢介面「等房間層真的要顯示成員名單時再設計，屆時才知道需要的是單筆還是批次」。本層需要的是**單筆讀自己**（`GetDisplayNameAsync(userId)`）。**批次讀 N 個成員仍然沒有被回答**——那是 `RoomJoined.member_user_ids` 要顯示成名字時才會逼出來的，屬於 webClient 的需求，見 §9。

### ADR-4：PostgreSQL 作為正式持久儲存，房間與封鎖名單一起遷入

- **Context**：`room-layer.md` ADR-7 把 `IRoomStore` / `IRoomBanList` 的 Redis 實作標為 provisional 並指定「跟聊天層的訊息記錄一起決定」。候選是 PostgreSQL、MongoDB、ScyllaDB/Cassandra，或維持 Redis。
- **Decision**：**PostgreSQL**。`rooms`、`room_bans`、`messages` 三張表同一個資料庫。
- **Consequences**：
  - **ScyllaDB/Cassandra 的形狀對訊息最好但對房間最差**。`partition key = room_id, clustering key = order_key desc` 是聊天訊息的教科書形狀，但房間列表需要條件查詢（`ListOpenAsync`）與條件更新（`TryCloseAsync` 的「已經關閉則回 false」），那是 Cassandra 最弱的地方（LWT 很貴）。選它等於還是要第二個儲存——**直接違反「一次決定」的初衷**。而且 `product-scope.md` §4 沒有任何規模數字，為還沒發生的寫入量付營運複雜度不划算。
  - **MongoDB 的 schema 彈性在這裡不值錢**：訊息的欄位在 §6.2 就固定了，未來要加圖片／reaction 是新功能而不是 schema 演進；而房間那種「唯一性 + 條件更新」的保證在 Mongo 要自己小心。
  - **維持 Redis 等於這個決定沒做**——那正是 ADR-7 當初標 provisional 的原因（持久資料放快取型儲存、訊息無限成長且全在記憶體、分頁要自己用 Sorted Set 疊）。
  - **Postgres 的代價是誠實的**：單表寫入有上限，真要衝量得自己分片。接受這個代價的前提就是「沒有規模數字」。
  - **而現在「換掉」是真的做得到的**：ADR-2 改成 append-only 之後，`IChatMessageStore` 的三個方法（append / range scan / range delete）任何 key-value 或文件儲存都給得起。**初稿宣稱同一件事但那是假的**，它要求交易內的 RMW。這句話從願望變成事實，是本次重寫最實質的成果。
  - **Session / Presence / 成員名單留在 Redis**，不遷。它們本來就是 key-value + TTL 語意，`identity-layer.md` §9 那條「屆時要不要一起遷」的答案是**不要**：Session 有 sliding TTL、Presence 是 Supersede 的 compare-and-swap、成員名單有寬限期的 Sorted Set，這三個放進關聯式資料庫全部會變難看且變慢。

### ADR-5：`chat.send` / `chat.history` 都不帶 `room_id`，走 WebSocket 命令而不是 HTTP

兩個決定綁在一起，因為它們的理由是同一個。

- **Context（路徑）**：歷史分頁天生是 request/response 的形狀，放 `WebBff` 的 HTTP endpoint 很自然，還能用 HTTP caching、而且不會佔用 WebSocket。另一邊是註冊成 `chat.history` 命令，跟其他所有命令一致。
- **Context（`room_id`）**：所有房間層命令都帶 `room_id`。聊天命令照做是最一致的寫法。
- **Decision**：走 **WebSocket 命令**；請求 **不帶 `room_id`**，伺服器用 `GetCurrentRoomAsync(userId)` 自己決定。
- **Consequences**：
  - **一次查詢同時是授權、定址與「房間還開著嗎」**。「使用者一次只能在一間房」（`product-scope.md` §6 已確認的產品決定）讓「他在哪間房」成為伺服器知道的事實。client 給 `room_id` 的話，每個 handler 都要多一步「你真的在這間房嗎」，而那步驟一旦漏掉就是**可以讀取任意房間歷史訊息**的漏洞。不帶就不可能漏。
  - **走 HTTP 的話這個性質會消失一半**：`WebBff` 目前只連 Redis + NATS，要多連 Postgres；「你是不是這房間的成員」的授權要在 `WebBff` 再寫一次。**同一條授權規則寫在兩個地方**是這裡真正的成本。
  - **代價**：一次歷史查詢會佔住那條連線的順序通道（`protocol-layer.md` ADR-2），期間使用者送不出訊息。一頁 50 則的 keyset 查詢走主鍵，是毫秒級——但這是一個**有觸發條件的決定**：如果日後要做全文搜尋（完全不同的查詢成本），就該把它搬到 HTTP，屆時要連授權一起搬。
  - **副作用**：使用者不在任何房間時 `chat.history` 回 `NOT_IN_ROOM`——**沒有辦法查詢自己不在的房間的歷史**。這造成 §9 那個關房後歷史查不到的洞。

### ADR-6：保留期限用分批 `DELETE`，時間分區列為條件觸發

- **Context**：訊息保留 90 天後要清理。兩個做法：(a) `PARTITION BY RANGE (sent_at)` 月分區，清理是 `DROP TABLE`；(b) 分批 `DELETE`。
- **Decision**：**(b)**，由 `ChatRetentionSweeper` 每天跑一輪，每批 5000 列，刪到當輪沒有東西可刪為止。分區列為條件觸發的後續決定。
- **Consequences**：
  - **分區的代價是現在就要付的**，而 `DELETE` 的代價是量大了才會痛：分區表的主鍵**必須包含分區鍵**，所以 `(room_id, order_key)` 得變成 `(room_id, order_key, sent_at)`；而 §5.2 的查詢條件裡沒有 `sent_at`，planner 無法做分區裁剪，每次翻頁都要掃過所有分區。換句話說 (a) 會讓**唯一的讀取路徑變慢**，換來一個目前不存在的清理問題被解決。這跟 `room-layer.md` §8「房間列表的分頁先不做，等數量成為問題再說」是同一種判斷。
  - **Sweeper 必須是 idempotent**，理由跟 `RoomGraceSweeper` 完全一樣：`CommandRouter` 是多複本，每個複本都跑自己的 sweeper。`DELETE ... WHERE sent_at < $cutoff` 天生 idempotent，兩個複本同時跑最多是白做工。
  - **正確性不依賴 sweeper**：它掛掉的後果是磁碟成長，不是查詢結果錯誤。所以不需要對應的「讀取時過濾」機制——**過期但還沒被刪的訊息仍然查得到，這是可接受的**。
  - **90 天是暫定值，未經任何驗證**，處理方式比照 `room-layer.md` 的 30 秒寬限期。
  - **觸發條件**：當單輪清理的時間長到影響正常查詢（鎖競爭、autovacuum 追不上造成的 bloat），就改用 (a)。需要實測數據。

### ADR-7：送訊息的限流與長度上限放 handler，**不**放 `IInboundFilter`

- **Context**：`protocol-layer.md` ADR-6 的 `IInboundFilter` 機制目前**沒有任何使用者**——它原本是為身分層的「未綁定前只接受 `identity.bind`」而建，那個狀態隨 handshake 驗證消失了。該 ADR 自己寫下了了斷條件：「它的下一個候選是 per-user 業務限流……如果限流最後也不放這裡，就該考慮把 `IInboundFilter` 整個拿掉。」**本層就是那個候選到期的時刻。**
- **Decision**：**限流要做**，但**放在 `ChatSendHandler` 裡，不用 filter**。
- **為什麼要做**（本文件初稿判斷「demo 不需要限流」，那個判斷是錯的）：`chat.send` 是**第一個會寫入無上限成長的持久儲存的命令**。房間層那 8 個命令全部是有界操作——建房、加入、踢人，狀態量不隨呼叫次數成長。灌訊息會直接吃磁碟。這跟連線層加 256 KB 訊息大小上限是同一類防護，而 `protocol-layer.md` ADR-8 已經明說「濫用不靠 terminate 防，靠限流與訊息大小上限」。**觸發它的不是吞吐量，是持久儲存。**
- **為什麼不用 filter**：`FilterDecision` 只有 `Allow` / `Drop` / `Terminate`，**沒有辦法回一則訊息給 client**。而 `protocol-layer.md` ADR-8 對上層的隱含要求正是「業務失敗必須用明確的下行訊息回覆，否則 client 會什麼都收不到、看起來像卡住」。被限流的 client 收到 `Drop` 就是沉默——正是那條要求要避免的狀況。
- **Consequences**：
  - 失去「在 payload 解析之前擋掉」的好處。但那個好處很小：payload 解析是一次小訊息的 protobuf parse，真正的成本是 Postgres 寫入，而那在限流檢查之後。
  - **`IInboundFilter` 因此仍然沒有使用者**，而它預告的最後一個候選已經用掉了。照 `protocol-layer.md` ADR-6 自己設的條件，**現在應該把它移除**——但那是協定層的變更，不由本文件決定，記在 §10。
  - 限流用 Redis 計數（`CommandRouter` 多複本，記憶體計數器不跨節點）：`INCR Chat:rate:{userId}:{unixSecond}` + `EXPIRE 2`。每則訊息多一次 Redis 往返，加上 ADR-3 那次 `HGET`，熱路徑上總共兩次。
  - **長度上限在領域模型裡**（`ChatMessageDraft.MaxBodyLength`，6.1），不是靠連線層的 256 KB——那個上限防的是記憶體與 NATS `max_payload`，不是「一則聊天訊息該多長」。

### ADR-8：業務規則留在 handler，刻意接受毫秒級的 TOCTOU

- **Context**：初稿把「房間已關閉不能發言」寫進 `UPDATE rooms SET ... WHERE room_id = $1 AND NOT is_closed`，理由是消除 TOCTOU——先查再寫的話，查完之後房間被關掉，訊息還是進得去。
- **Decision**：**不消除它**。業務規則回到 handler，`chat.send` 只做 `GetCurrentRoomAsync`，接受「關房正在進行中」那個毫秒級窗口。
- **Consequences**：
  - **規則變回看得見的**。藏在 WHERE 子句裡的規則，讀 handler 的人不會知道它存在；而 handler 是所有人排查問題時會先打開的檔案。
  - **窗口比想像中更小**：`room.close` 會清空成員名單（`room-layer.md` §9），所以關房之後 `GetCurrentRoomAsync` 本來就回 `null`。殘留的競爭只有「關房流程跑到一半、成員還沒被清」那一瞬間。
  - **後果無害**：最多一則訊息寫進一間剛關閉的房間，躺在歷史裡，而房間已關沒有人在收。對照 `room-layer.md` 6.1 那份「刻意接受的競爭」清單（兩個 `TryCloseAsync` 併發會廣播兩次 `RoomClosed`），這一條的嚴重性更低。
  - **少一個狀態、少一次查詢**：`ChatOperationReply` 不需要 `ROOM_CLOSED`，handler 也不需要讀 `IRoomStore`。**拿掉資料庫中心的設計讓這一層變小了。**
  - **邊界要說清楚**：這條 ADR 說的是「業務規則」，不是「所有一致性都可以放棄」。`room-layer.md` 6.1 的判準仍然適用——那裡也只有 `TryCreateAsync` 一個操作需要真正原子，其餘全部列在「刻意接受的競爭」底下。本層目前沒有任何操作需要真正原子。

### ADR-9（條件觸發）：房間 actor 接手發號與成員狀態

- **Context**：ADR-2 放棄了連續序號，代價是 client 無法偵測漏訊息。要拿回連續性，正確的做法不是把不變量塞回資料庫（那是被推翻的 (d)），而是讓**房間成為一致性邊界**——同一房間的命令全部由同一個擁有者處理，順序自然落下來，不需要鎖也不需要交易。使用者已表明打算在需要時採用 **Dapr actor**。
- **Decision（暫緩）**：目前不做。`IMessageSequencer` 是預留的接縫。
- **它會買到什麼**：
  - 連續的 `order_key`（turn-based 讓計數器不共享，寫入失敗時回退記憶體計數器是安全的——這正是 Redis `INCR` 做不到的事）。
  - **`RoomGraceSweeper` 可以消失**。`room-layer.md` ADR-2 明寫那個 10 秒輪詢存在的原因是「目前技術棧沒有排程能力（core NATS 沒有延遲投遞、也沒有引入 JetStream）」，而 actor reminder 就是持久化排程。
  - 成員名單可以住在 actor 記憶體裡，每則訊息現在那兩次 Redis 往返（`GetMembersAsync` + `ResolveConnectionsAsync`）會消失。
- **Dapr 解決掉的成本**（自寫分區擁有者要付、Dapr 不用付的）：
  - **位置透明性讓「進入點必須知道房間」的要求消失**。`InboundBridge` 住在 Gateway 上、手上只有 `connectionId` 與 `principal`；自寫分片就得讓 Gateway 查房間（把 `room-layer.md` ADR-3 剛拆掉的耦合裝回去）或讓 client 傳 `roomId`（等於 client 選複本，`protocol-layer.md` ADR-3 拒絕過）。用 actor 的話任何複本都能收下訊息、做它今天就在做的那次查詢、然後 `ActorProxy` 呼叫，`command.inbound` 的 subject 與 queue group 完全不動。
  - **placement service 取代自寫的 lease + heartbeat + rebalance。**
  - 順序不受兩跳影響：`protocol-layer.md` ADR-2 已經從結構上保證單一連線同時只有一則 in-flight，該有序的部分已經有序。
- **Dapr 不解決的**（不要高估它）：
  - **單一啟動在網路分區下沒有保證**——placement 更新或分區期間可能短暫出現兩個 activation，要讓連續性滴水不漏仍然需要 fencing。
  - **sidecar 在熱路徑上**，每則訊息多兩跳。
  - **placement service 是新的有狀態基礎設施**，要自己 HA。成本是從**程式碼**裡消失，不是從**維運**裡消失。
  - **它會是 NATS/Adaptare 旁邊的第二套執行期**，而 `room-layer.md` §9 剛剛才得出「系統只有一套 messaging」的結論；`protocol-layer.md` ADR-5 拒絕過的正是「同一件事有兩套機制」。
- **觸發條件**：(1) 缺口偵測變成真的需求（而不是 nice-to-have）；或 (2) 每則訊息那幾次 Redis 往返成為量測到的瓶頸；或 (3) 系統因為別的理由決定採用 Dapr。
- **Consequences**：
  - **這是系統級決定，不是聊天層的決定**——它會動到房間層（成員名單、寬限期、fencing 全部想搬進 actor）與協定層。照層次紀律不該從本層偷渡進來，所以記成條件觸發，跟 `connection-layer.md` ADR-5、`protocol-layer.md` ADR-7 同一種格式。
  - 落地時本層要改的只有兩處：`IMessageSequencer` 的註冊、以及 `order_key` 語意從「稀疏」變成「連續」。**`IChatMessageStore` 一行都不用改**（append-only 正是 actor 要的形狀）。
  - **語意轉換不會自然銜接**：actor 之前寫入的訊息是稀疏鍵，之後是連續鍵，舊資料無法回溯。缺口偵測只能對「切換點之後」的訊息開啟，那個切換點屆時要明確處理。

## 8. 明確排除於本階段

- **私訊 / 1-on-1**：`product-scope.md` §3 已排除。`main` 的 `ChatMessage.Scope` 因此不在本層的 proto 裡——**這是刻意不留擴充點**：留一個只有一個值的 enum 只會讓人以為那條路走得通。要做私訊時 fan-out 的名單來源完全不同，本來就不是加一個欄位的事。
- **系統訊息**（`main` 的 `Scope.System`）：房間層已經有 `RoomMemberJoined` / `RoomMemberLeft` / `RoomKicked` / `RoomClosed` 這些具名事件，client 自己決定要不要在訊息流裡呈現成一行系統訊息。塞進 `ChatMessage` 會讓它們同時存在兩種形式。
- **訊息的編輯與刪除**：`product-scope.md` 沒有提。
- **附件 / 圖片 / reaction / 已讀回條 / typing indicator**：都不在 `product-scope.md` §2 的核心功能範疇裡。
- **全文搜尋**：`product-scope.md` §6 列為「還沒討論」。它的查詢成本跟 keyset 分頁完全不同量級，會直接推翻 ADR-5 走 WebSocket 的判斷。
- **缺口偵測與訊息的投遞保證**：core NATS 沒有 retry（`protocol-layer.md` §8），在線期間丟一則下行訊息就是丟了，而 `order_key` 稀疏所以 client **偵測不到**（ADR-2）。要拿回來見 ADR-9。
- **房間人數上限**：`room-layer.md` §8 已排除，fan-out 的規模因此仍然沒有上界。

## 9. 待確認 / 後續事項

- **關閉的房間，歷史訊息沒有人有權限查**。這是 ADR-5 與房間層互動出來的洞，兩邊各自都合理：
  - `room-layer.md` 6.1 選「`IsClosed` 而不是真刪」，理由明寫是「歷史訊息如果變成孤兒就麻煩了」；
  - 但該文件 §9 又寫「`room.close` 先讀名單、再關房、廣播後才清成員」——**關房後成員名單是空的**；
  - 而 ADR-5 的授權是「你現在在這間房嗎」。三者疊起來：資料還在、保留期內不會被刪、**但沒有任何人查得到**。
  - 三個方向（都沒定案）：關房時保留成員名單只清 `userRoom` 反向指向；或給歷史查詢一條「曾經是成員」的授權路徑（需要一張參與紀錄表）；或接受現狀並改掉 `room-layer.md` 6.1 那句理由。**第三個最誠實但也最沒用**，那等於承認關房＝歷史消失。
- **`IUserProfileStore` 只被回答了一半**（ADR-3）。本層需要單筆的 `GetDisplayNameAsync(userId)`；**批次讀 N 個成員的 profile 仍然沒有介面**，而 `RoomJoined.member_user_ids` 要在 UI 上顯示成名字就需要它。答案是**兩個都要**，但批次那個要等 webClient。
- **房間層的 Redis 遷移完成後，`room-store` 這個資源要怎麼處理**。`rooms` / `room_bans` 搬進 Postgres 之後，`room-store` 只剩 `IRoomMembership`（成員名單、寬限期的 Sorted Set）。保留它（符合 `connection-layer.md` ADR-2 的「每層擁有自己的基礎設施」）或併進 `connection-directory`（同樣是暫時狀態）。傾向後者，但跟 ADR-2 的原則衝突——**沒定案**。
- **遷移是一次破壞性變更**：`RedisRoomStore` / `RedisRoomBanList` 會被 Postgres 版本取代，`room-layer.md` 6.1 那張 Redis key 設計表會整段作廢。那節的論證仍有保存價值（它解釋了為什麼 `{rooms}` 用 hash tag 而連線層刻意不用），應該改寫成「已被取代」而不是刪掉。**注意：`{rooms}:seq:*` 這個 key 從來沒有存在過**——它只出現在本文件被推翻的第一版設計裡（ADR-2 的 (b)）。
- **`IInboundFilter` 的去留**（ADR-7）：協定層的變更，需要獨立決定。
- **暫定值**（全部未經負載測試）：保留期 90 天、清理批次 5000 列 / 每天一輪、每人每秒 10 則、每則 4096 字元、歷史分頁預設 50 則。
- **測試分層**：
  - `Integration.Tests`（`Adaptare.Direct`、in-memory 替身）可以蓋掉本層**絕大部分**行為——這是 append-only 帶來的好處：初稿的「seq 連續無洞」與「同房序列化」是 Postgres 交易的性質，in-memory 替身怎麼寫都會通過，等於在測自己寫的替身。現在沒有這個問題。
  - 仍然需要真 Postgres 的只剩兩件：**主鍵衝突時 `TryAppendAsync` 真的回 `false`**（6.4 的重試路徑，替身不會自然產生 `23505`），以及 **keyset 分頁在稀疏鍵上的實際查詢計畫**。放 `E2E.Tests` 或用 Testcontainers。
  - **`MonotonicMicrosecondSequencer` 的 CAS 迴圈要有併發測試**：多執行緒同時呼叫 `Next()`，斷言結果全部相異且嚴格遞增。非原子的版本在單執行緒測試下永遠會過（6.4）。

## 10. 對既有文件的影響

### `room-layer.md`

- **ADR-7 的 provisional 狀態結束**：`IRoomStore` / `IRoomBanList` 遷入 PostgreSQL（本文件 ADR-4）。
- **`Room` record 不需要新欄位**。本文件第二版曾要求加一個 `LastSeq`，那隨 ADR-2 的推翻一起取消——**訊息的寫入路徑完全不碰房間資料**。
- **6.1 的 Redis key 設計表作廢**，但論證要保留（見 §9）。
- **§9 那條「關閉房間之後歷史訊息要保留多久、還能不能查」有答案了，但答案暴露了一個洞**（見 §9 第一條）。該節寫「會回頭影響 `IsClosed` 而非真刪的設計是否足夠」——確實不夠。
- **ADR-2 的 `RoomGraceSweeper` 在本文件 ADR-9 落地時可以消失**（actor reminder 取代輪詢）。該 ADR 明寫輪詢存在的原因就是「沒有排程能力」。
- `RoomBroadcaster` 多一個使用者（本文件 6.8），行為不變。

### `protocol-layer.md`

- **ADR-6 的了斷條件到期**：`IInboundFilter` 預告的最後一個候選（per-user 業務限流）確定不放這裡（本文件 ADR-7）。變更本文件不做，但條件已成立。
- **§9「per-subject／per-user 業務限流：等有業務規則再做」有業務規則了**，但落點是 handler 而不是該節寫的 filter。
- ADR-2 的「單一連線 1/RTT」第一次有了會實際觸碰它的命令（`chat.history`），觸發條件記在本文件 ADR-5。而它同時也是本文件 ADR-9 成立的前提——單一連線的順序已經被結構性地保證，所以房間 actor 那條路上的「兩跳」不會破壞順序。
- **ADR-7（依 `connectionId` 分片）與本文件 ADR-9（依 `roomId` 分區）互斥**：分區鍵只能選一個。兩條要一起評估。

### `identity-layer.md`

- **ADR-11 的查詢介面部分定案**：需要單筆的 `GetDisplayNameAsync(userId)`（本文件 ADR-3）。批次版本仍然待定。
- **§9 那條「Session／profile 要不要跟房間資料一起遷到正式儲存」有答案了：不遷**（本文件 ADR-4）。

### `connection-layer.md`

- 不需要修改。本層沒有對連線層提出任何新需求——這是四層以來第一次。
- 但 `Gateway/Program.cs:35` 那個 `Guid.NewGuid()` 的 nodeId 在本文件 ADR-2 的 (e) 被引用為「snowflake 不可行」的理由之一，那個事實值得知道。

### `product-scope.md`

- §5 表格：聊天層狀態從「待設計」改為「設計中」。
- §6「訊息記錄要保留多久」已回答：90 天（暫定值）。「要不要分頁查詢」已回答：keyset 分頁。「要不要搜尋」**仍然沒做**。
- §6「房間本身與封鎖名單需要持久儲存，這跟訊息記錄是同一個儲存決定」已執行：PostgreSQL。**注意該節先前引用的理由（「`seq` 的連續性要求同交易遞增」）隨 ADR-2 一起被推翻，要改回原本那個較弱但正確的理由：同一套 migration／備份策略。**
- §4「沒有具體規模數字」在本層被引用了三次（ADR-4 選 Postgres、ADR-6 不做分區、ADR-9 暫緩 actor）。
