# 協定層架構設計（Protocol Layer）

狀態：討論中 draft，尚未實作
技術棧：延續連線層的 .NET + NATS（Adaptare）+ protobuf
範圍：**client 封包內容的解析、subject 與型別的對應、分派給對的 handler**，不含任何具體命令的業務語意

## 1. 範圍界定

這層要解決的問題只有四個：

1. **解析**：把連線層交出來的不透明 `payload` 轉成具體的 protobuf 訊息型別。
2. **分派**：依 subject 找到該處理它的 handler，取代單一 `IInboundMessageHandler` 插槽裡不斷增長的 if/else。
3. **協定層級的錯誤政策**：未知 subject、payload 畸形、handler 例外，各自該怎麼處置，只在一個地方定義。
4. **跨切面著力點**：per-subject 觀測、per-connection 限流、命令的前置條件檢查（例如「未綁定身分前只接受 `identity.bind`」）。

**與連線層的邊界**（延續既有決定，本文件不改動它）：`Packet` 信封（`subject` + `payload`）的組裝與拆解仍屬連線層——`GatewayWebSocketEndpoint` 拆、`Connection.SendAsync` 組。連線層看得到 `subject` 但不解讀其語意，`payload` 全程是不透明的 `ByteString`。協定層接手的正是 `payload` 的型別對應與內容解析。

明確排除：具體命令要做什麼（身分綁定屬身分層、房間/聊天屬更上層，它們只是在這層註冊 handler）、連線本身的維護（連線層）、下行的路由與 fan-out（`Dispatcher`）。

## 2. 現有實作對照

**`main` 分支**——沒有「統一入口」的概念，解析散在 Connector、分派靠 NATS subject：

- `ChatConnector/Models/ClientConnectHandler.cs` `OnReceiveAsync`：解析 `Packet` 後包成 `SendQueueCommand` 就丟出去，Connector 本身不認識任何命令。
- `ChatConnector/Models/SendQueueCommandService.cs`：`messageQueueService.PublishAsync(command.Subject, ...)`——**`command.Subject` 完全來自 client 封包**，等於客戶端可以決定訊息要發布到哪個內部 NATS subject。內容會先被包一層 `QueuePacket`，所以不一定能構成有效的內部訊息，但「客戶端能決定發布目標」本身就是不該存在的能力。這是 ADR-3 要避免的東西。
- 同一個 `OnReceiveAsync` 裡 `httpContext.RequestServices.CreateScope()` 沒有 `using`，scope 不會被釋放。

**`feature/rebuild` 現況**：

- `Gateway/Models/NoOpInboundMessageHandler.cs`：插槽佔位，只記 log。
- `docs/architecture/identity-layer.md` 第 6.3 節的 `if (subject != "identity.bind") return; // 之後有其他 subject 時在這裡分派`——這行註解就是本文件要取代的設計。單一插槽會讓每個新增功能都去改同一個檔案。

## 3. 核心概念

| 概念 | 職責 |
|---|---|
| `InboundPacket`（proto，新） | Gateway → CommandRouter 的信封：`connection_id` + `subject` + `payload` |
| `InboundAck`（proto，新） | CommandRouter → Gateway 的處理結果。**只用於順序控制與觀測**，Gateway 不依據它做任何動作（見 ADR-5） |
| `InboundBridge`（協定層元件，寄宿在 Gateway process） | 取代 `NoOpInboundMessageHandler` 的 DI 註冊。把 `(connectionId, subject, payload)` 用 request/reply 送給 CommandRouter，收到 ack 才讓 receive loop 讀下一個 frame |
| `CommandRouter`（新服務） | 訂閱 `command.inbound`（掛 queue group），跑 filter pipeline → 查 registry → 解析 → 分派 |
| `PacketRegistry` | `subject ↔ 訊息型別` 的唯一對應表，**入站解析與出站序列化共用同一份** |
| `IPacketHandler<TMessage>` | 各層/各功能自己實作的命令 handler，向 CommandRouter 註冊 |
| `IInboundFilter` | 分派前的前置條件檢查，只看得到 `connectionId` + `subject`，看不到 payload（見 ADR-6） |
| `IPacketPublisher` | 出口：把 typed 訊息轉成 `(subject, bytes)` 後呼叫既有的 `IOutboundGateway`，下行一律經 `Dispatcher`（見 ADR-5） |

## 4. 元件關係圖

```mermaid
graph TB
    subgraph Client
        WC[WebClient]
    end

    subgraph "Gateway（連線層，程式碼不變）"
        WS[GatewayWebSocketEndpoint]
        IB["InboundBridge\n(協定層元件，寄宿於此\n取代 NoOpInboundMessageHandler)"]
    end

    subgraph "CommandRouter（協定層，新服務）"
        IP[InboundProcessor]
        FP["IInboundFilter pipeline"]
        RG[("PacketRegistry\nsubject ↔ 型別")]
        H1["IPacketHandler&lt;BindRequest&gt;\n(身分層註冊)"]
        H2["IPacketHandler&lt;...&gt;\n(未來業務層註冊)"]
    end

    subgraph "連線層既有的下行能力"
        OG[IOutboundGateway]
        CT[IConnectionTerminator]
        DP[Dispatcher]
    end

    WC -- "WebSocket Packet" --> WS
    WS -- "IInboundMessageHandler\n(connectionId, subject, payload)" --> IB
    IB -- "request/reply\ncommand.inbound" --> IP
    IP --> FP
    FP --> RG
    RG --> H1
    RG --> H2
    H1 -- "回訊息給 client" --> OG
    H1 -- "踢掉舊連線" --> CT
    OG -- "dispatch.deliver" --> DP
    CT -- "dispatch.terminate" --> DP
    DP -- "connect.deliver.{nodeId}\nconnect.terminate.{nodeId}" --> WS
    IP -. "InboundAck" .-> IB
```

## 5. 訊息序列

### 5.1 一則 inbound 命令的完整路徑

```mermaid
sequenceDiagram
    participant WC as WebClient
    participant WS as GatewayWebSocketEndpoint
    participant IB as InboundBridge
    participant RT as CommandRouter
    participant H as IPacketHandler

    WC->>WS: WebSocket frame（Packet）
    WS->>WS: Packet.Parser.ParseFrom（連線層：只拆信封）
    WS->>IB: HandleAsync(connectionId, subject, payload)
    IB->>RT: RequestAsync("command.inbound", InboundPacket)
    RT->>RT: filter pipeline
    RT->>RT: registry 查 subject → 型別
    RT->>RT: TMessage.Parser.ParseFrom(payload)（協定層：解內容）
    RT->>H: HandleAsync(connectionId, message)
    H-->>RT: 完成
    RT-->>IB: InboundAck(OK)
    IB-->>WS: 回到 receive loop，才讀下一個 frame
```

最後兩步是 ADR-2 的重點：因為 receive loop 是「處理完才讀下一個 frame」（`Gateway/Services/GatewayWebSocketEndpoint.cs:84`），單一連線同一時間最多只有一則訊息在 in-flight，端到端順序因此被保證。

### 5.2 handler 產生下行訊息

```mermaid
sequenceDiagram
    participant H as IPacketHandler
    participant PP as IPacketPublisher
    participant OG as IOutboundGateway
    participant DP as Dispatcher
    participant GW as 目標連線所在的 Gateway 節點
    participant WC as WebClient

    H->>PP: PublishAsync(connectionIds, BindReply)
    PP->>PP: registry 反查型別 → subject
    PP->>OG: DeliverAsync(subject, connectionIds, payload)
    OG->>DP: dispatch.deliver
    DP->>DP: ResolveNodesAsync 依 NodeId 分組
    DP->>GW: connect.deliver.{nodeId}
    GW->>WC: Packet（連線層組信封）
```

`IPacketPublisher` 只做「型別 → subject + 序列化」，之後完全走連線層既有路徑，沒有任何新的下行通道。

### 5.3 協定違反 → 終止連線

```mermaid
sequenceDiagram
    participant WC as WebClient
    participant IB as InboundBridge
    participant RT as CommandRouter
    participant CT as IConnectionTerminator
    participant DP as Dispatcher
    participant GW as 連線所在的 Gateway 節點

    WC->>IB: payload 畸形 / filter 判定拒絕
    IB->>RT: RequestAsync(InboundPacket)
    RT->>CT: TerminateAsync([connectionId])
    CT->>DP: dispatch.terminate
    DP->>GW: connect.terminate.{nodeId}
    GW->>WC: WebSocket Close
    RT-->>IB: InboundAck(MALFORMED_PAYLOAD)
    Note over IB: 只記 log，不自己關連線（ADR-5）
```

這裡有個要注意的交錯：等待 ack 的 Gateway 節點，跟收到 terminate 的 Gateway 節點是同一個。`Connection.CloseOutputAsync` 會在 receive loop 還在 await ack 時把 close frame 送出去，socket 狀態變成 `CloseSent`；ack 回來後 receive loop 檢查 `connection.State == WebSocketState.Open` 不成立就自然結束，`finally` 照原本流程跑 `OnDisconnectedAsync`。兩者不會互相踩到——`Connection` 的 `m_SendLock` 已經保證送出類的呼叫互相排隊。（連線層刻意用 `CloseOutputAsync` 而非 `CloseAsync`，正是為了不跟 receive loop 搶同一個 receive，見 `connection-layer.md` 第 6.6 節。）

## 6. 具體介面設計

### 6.1 Proto（`Common/Protos/protocol.proto`，新檔案）

`connection.proto` 保持不動——那是連線層自己的檔案，下面這些是協定層自己的傳輸格式：

```protobuf
syntax = "proto3";
option csharp_namespace = "Chat.Protos";
package chat;

// Gateway（InboundBridge）-> CommandRouter：一則來自 client 的封包，附上它來自哪條連線。
message InboundPacket {
	string connection_id = 1;
	string subject = 2;
	bytes payload = 3;
}

// CommandRouter -> Gateway（InboundBridge）：處理結果。
// 只用於順序控制與觀測，Gateway 不依據它做任何動作（見 ADR-5）。
message InboundAck {
	enum Status {
		OK = 0;
		UNKNOWN_SUBJECT = 1;
		MALFORMED_PAYLOAD = 2;
		REJECTED_BY_FILTER = 3;
		HANDLER_FAILED = 4;
	}

	Status status = 1;
	string detail = 2;
}
```

### 6.2 `InboundBridge`（協定層元件，寄宿在 Gateway process）

```csharp
namespace CommandRouter; // 共用抽象放 Common.Protocol，見 6.3 節

// 取代 Gateway/Program.cs 裡 NoOpInboundMessageHandler 的 DI 註冊。
// 這是協定層的元件、只是寄宿在 Gateway process，連線層程式碼不需要任何修改。
internal sealed class InboundBridge(
	IMessageSender messageSender,
	ILogger<InboundBridge> logger) : IInboundMessageHandler
{
	private const string InboundSubject = "command.inbound";
	private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(5); // 值待確認，見第 9 節

	public async ValueTask HandleAsync(
		string connectionId,
		string subject,
		ByteString payload,
		CancellationToken cancellationToken = default)
	{
		var packet = new InboundPacket
		{
			ConnectionId = connectionId,
			Subject = subject,
			Payload = payload
		};

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_Timeout);

		try
		{
			var reply = await messageSender
				.RequestAsync<byte[], byte[]>(InboundSubject, packet.ToByteArray(), timeout.Token)
				.ConfigureAwait(false);

			var ack = InboundAck.Parser.ParseFrom(reply);

			if (ack.Status != InboundAck.Types.Status.Ok)
				logger.LogWarning(
					"{ConnectionId} {Subject} rejected: {Status} {Detail}",
					connectionId, subject, ack.Status, ack.Detail);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// CommandRouter 沒回應。這個例外往上丟之後會被連線層當成正常關站吞掉
			// （GatewayWebSocketEndpoint.cs:36），所以 log 一定要在這裡記。
			logger.LogError("{ConnectionId} {Subject} timed out waiting for the command router.", connectionId, subject);
			throw;
		}
	}
}
```

用 `RequestAsync<byte[], byte[]>` 而非泛型 protobuf 型別，是沿用 `DispatchHandler`/`DeliverPacketHandler` 既有的「Adaptare 只搬 `byte[]`、protobuf 自己解」慣例。

### 6.3 `PacketRegistry` 與 `IPacketHandler<TMessage>`

```csharp
namespace Common.Protocol;

public interface IPacketHandler<TMessage> where TMessage : IMessage<TMessage>
{
	// protobuf 產生的類別沒有 static abstract Parser，由實作端明白交出來：
	//   public static MessageParser<BindRequest> Parser => BindRequest.Parser;
	static abstract MessageParser<TMessage> Parser { get; }

	ValueTask HandleAsync(string connectionId, TMessage message, CancellationToken cancellationToken = default);
}
```

註冊時把 subject 一起宣告，讓每個 subject 字面值在整個 codebase 只出現一次：

```csharp
// 身分層在 CommandRouter 的組裝處註冊自己的命令
services.AddPacket<BindRequest>("identity.bind").WithHandler<IdentityBindHandler>();

// 只出現在下行的訊息型別也要宣告 subject，這樣 IPacketPublisher 才反查得到
services.AddPacket<BindReply>("identity.bind.reply");
```

`PacketRegistry` 是這些註冊的彙總，同時服務兩個方向：`subject → (parser, handler)` 給入站用，`型別 → subject` 給出站用。啟動時對重複 subject 直接 fail fast，並把整份對應表寫進 log（緩解 ADR-4 失去編譯期檢查的代價）。

### 6.4 `IInboundFilter`

```csharp
namespace Common.Protocol;

public enum FilterDecision
{
	Allow,
	Drop,      // 忽略這則封包，連線保留
	Terminate, // 視為協定違反，CommandRouter 統一呼叫 IConnectionTerminator
}

public interface IInboundFilter
{
	int Order { get; }

	// 刻意只給 connectionId 與 subject：filter 是前置條件檢查，不該需要理解 payload。
	ValueTask<FilterDecision> EvaluateAsync(
		string connectionId,
		string subject,
		CancellationToken cancellationToken = default);
}
```

處置動作（`IConnectionTerminator`、log、metrics）一律由 CommandRouter 統一執行，filter 只回傳判斷。身分層的「未綁定前只接受 `identity.bind`」就是註冊一個這種 filter（見第 10 節：身分層需要補一個目前沒有的反向查詢）。

### 6.5 出口：`IPacketPublisher`

```csharp
namespace Common.Protocol;

public interface IPacketPublisher
{
	ValueTask PublishAsync<TMessage>(
		IReadOnlyCollection<string> connectionIds,
		TMessage message,
		CancellationToken cancellationToken = default)
		where TMessage : IMessage<TMessage>;
}
```

實作只有三行：registry 反查 subject → `message.ToByteString()` → 呼叫既有的 `IOutboundGateway.DeliverAsync`。呼叫端從此不再手寫 subject 字串、不再自己 `ToByteArray()`。

### 6.6 `CommandRouter` 服務的組裝樣貌

```csharp
var builder = Host.CreateApplicationBuilder(args);
{
	builder.AddServiceDefaults();

	// 下行要用 IOutboundGateway / IConnectionTerminator，兩者都要 Redis + NATS
	builder.AddRedisClient("connection-directory");
	builder.AddNatsClient("message-bus");
	builder.Services.AddConnectionDirectory();
	builder.Services.AddOutboundGateway();

	// 協定層自己
	builder.Services.AddPacketRegistry();

	// 各層註冊自己的命令與 filter
	builder.Services.AddIdentityPackets();

	builder.Services
		.AddMessageQueue()
		.AddNatsMessageQueue(config => config
			.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>())
			.AddProcessor<InboundProcessor>("command.inbound", "command.inbound")); // 第二個參數為 queue group
}
```

`InboundProcessor : IMessageProcessor<byte[], byte[]>`（Adaptare 的 request/reply handler 型別，`HandleAsync` 回傳 reply）。`AddProcessor` 的實際多載簽章待實作時對著套件確認，這裡沿用 `Dispatcher/Program.cs:20` 的 `AddHandler<DispatchHandler>("dispatch.deliver", "dispatch.deliver")` 寫法推導。

## 7. 架構決策記錄（ADR）

### ADR-1：協定層獨立成服務，Gateway 只保留一個 thin bridge

- **Context**：`IInboundMessageHandler` 是單一插槽。若讓各層直接在 Gateway process 內實作，Gateway 會逐漸變成業務邏輯的宿主，部署上跟業務綁在一起。
- **Decision**：協定層獨立成 `CommandRouter` 服務；Gateway 只寄宿一個 `InboundBridge`。
- **Consequences**：Gateway 維持純管道，跟下行的 `Dispatcher` 形成對稱；業務 handler 可以獨立更新、獨立擴展。代價有三個：每則 inbound 多一次網路 hop；`CommandRouter` 成為所有業務 handler 的宿主 process（未來某個業務要獨立擴展，再讓它訂閱自己的 subject 拆出去）；`CommandRouter` 全掛時 inbound 全斷，但下行（`Dispatcher`）不受影響。

### ADR-2：Gateway → CommandRouter 用 request/reply，不用 fire-and-forget publish

- **Context**：拆成獨立服務 + queue group 之後，同一條連線的兩則訊息會被分到不同複本並行處理，訊息順序不再保證。對聊天系統這是實質錯誤（聊天記錄順序錯亂），而且第一個踩到的是身分綁定——`identity.bind` 還沒處理完，後面的業務訊息就先被處理了。
- **Decision**：`InboundBridge` 用 `IMessageSender.RequestAsync` 送出並等待 `InboundAck` 才返回。因為連線層的 receive loop 是「`await` 完 `HandleAsync` 才讀下一個 frame」（`GatewayWebSocketEndpoint.cs:84`），單一連線同時最多一則訊息 in-flight，端到端順序因此被保證；不同連線之間仍完全並行（各自有獨立的 receive loop）。
- **Consequences**：不需要引入 `connectionId` 分片，`connection-layer.md` ADR-5 維持暫緩。代價：每則 inbound 多一次 NATS round-trip；單一連線的 inbound 吞吐上限變成 1/RTT（叢集內亞毫秒級，聊天場景遠遠夠用，但不適用高頻串流類的 subject）；`CommandRouter` 變慢或掛掉會直接反壓到 receive loop——這其實是想要的行為，避免 Gateway 無上限累積待處理訊息。

### ADR-3：固定使用單一 NATS subject `command.inbound`，不把 client 的 subject 拼進 NATS subject

- **Context**：更省事的做法是 `PublishAsync($"command.inbound.{clientSubject}", ...)`，讓 NATS 自己做分派、CommandRouter 連 registry 都不用寫。舊 `main` 分支正是這個方向，而且更徹底——`SendQueueCommandService` 直接把 client 給的 subject 當 NATS subject 用。
- **Decision**：固定單一 subject，分派由 `CommandRouter` 內部的 registry 負責。
- **Consequences**：客戶端無法決定訊息發布到哪個 NATS subject，內部 messaging 拓樸不再有一部分由客戶端輸入決定；未知 subject、payload 畸形、限流、per-subject 觀測都有唯一的著力點；「未綁定身分前只接受 `identity.bind`」這種跨命令規則也才有地方放（靠 NATS 分派的話每個 handler 都得自己檢查）。代價：`CommandRouter` 不能靠 NATS subject 做水平分流，所有 inbound 都經過同一個 queue group——要分流是加複本，不是加 subject。

### ADR-4：`subject ↔ 型別` 用 DI 收集的 registry，不用 `oneof` 大信封也不用 `Any`

- **Context**：三種常見做法——(a) registry；(b) 一個 `oneof` 涵蓋所有命令的大信封；(c) protobuf `Any` 自帶型別資訊。
- **Decision**：採用 (a)。
- **Consequences**：協定層本身不認識任何具體命令型別，新增命令只動自己模組的檔案，不會出現「所有功能都要改同一個 proto」的瓶頸（那是 (b) 的問題）；subject 保留為可讀的路由鍵與觀測維度（(c) 會讓 subject 變成冗余）；入站解析與出站序列化共用同一份對應表。代價：失去 `oneof` 的編譯期 exhaustiveness，漏註冊要到 runtime 才發現——用啟動時 fail fast + 印出整份對應表來緩解，但這確實比編譯期檢查弱。

### ADR-5：下行一律經 `Dispatcher`，協定層不自己開第二條下行通道

- **Context**：`InboundAck` 是 CommandRouter 回 Gateway 的既有通道，技術上可以順便夾帶「關掉這條連線」或「把這則訊息回給 client」，省一次繞行——特別是目標連線往往就在發出 request 的那個 Gateway 節點上。
- **Decision**：不這樣做。ack 只表達處理結果，僅用於順序控制與觀測。任何要送回 client 的訊息走 `IPacketPublisher` → `IOutboundGateway` → `dispatch.deliver` → `Dispatcher`；任何要關的連線走 `IConnectionTerminator` → `dispatch.terminate` → `Dispatcher`。
- **Consequences**：下行只有一條路徑，`InboundBridge` 完全不需要知道「投遞」或「終止連線」這些概念，維持 thin；也不會出現「同一件事有兩套機制、行為卻不完全一致」的長期維護問題。代價：即使目標連線就在原本那個節點上，訊息仍會繞 `CommandRouter` → `Dispatcher` → 同一個 Gateway 一圈，多一次 hop 加一次 Redis 查詢。這個浪費是刻意接受的。

### ADR-6：前置條件用 filter pipeline，由各層自己註冊

- **Context**：「未綁定身分前只接受 `identity.bind`」需要一個統一的著力點，但若由協定層直接實作，協定層就反過來依賴身分層。
- **Decision**：協定層只定義 `IInboundFilter`（只看得到 `connectionId` + `subject`），身分層自己註冊 filter；處置動作由 CommandRouter 統一執行。
- **Consequences**：依賴方向維持向下，協定層不認識身分概念，延續 `connection-layer.md` ADR-1 的精神。代價：身分層需要一個目前設計裡沒有的「`ConnectionId` → 是否已綁定」查詢（見第 10 節）。

### ADR-7（條件觸發）：`CommandRouter` 依 `connectionId` 分片

- **Context**：若 ADR-2 的「每連線 1/RTT」吞吐上限成為實際瓶頸。
- **Decision（暫緩）**：屆時才考慮由 `InboundBridge` 依 `hash(connectionId)` 分流到固定的 shard subject，改用 fire-and-forget 但保住每連線順序。
- **觸發條件**：需要實測數據證明 request/reply 的吞吐不足。這與 `connection-layer.md` ADR-5 是同一類決策，應該一起評估。

## 8. 明確排除於本階段

- 具體命令的業務語意：身分綁定屬身分層、房間/聊天屬更上層，本層只提供註冊與分派機制。
- WebClient / client SDK 端的封包封裝與下行分派。
- inbound 訊息的持久化與重送：目前用 core NATS（無 JetStream），處理失敗就是失敗，不設 retry queue。要不要引入 JetStream 是獨立議題。
- 連線斷開事件的對外通知（見第 9 節）。

## 9. 待確認 / 後續事項

- **已決定**：服務專案名為 `CommandRouter`，NATS subject 前綴為 `command.inbound`（沿用「前綴對應目標角色」的既有慣例：`dispatch.*` 給 `Dispatcher`、`connect.*` 給 Gateway 節點）。`Command` 這個字是用來跟 `Dispatcher` 區隔——`Dispatcher` 搬的是不理解內容的投遞封包，這個服務處理的是已解析成型別的命令；單獨叫 `Router` 會跟 `Dispatcher` 語意撞車（兩者幾乎同義，光看專案清單 `Gateway / Dispatcher / Router / Common` 猜不出哪個是上行哪個是下行）。排除 `Ingress`：k8s Ingress 有既定含義（HTTP 反向代理／入口控制器），會被誤認成基礎設施元件。排除 `Protocol`：協定層的共用抽象已經用 `Common.Protocol` 命名空間，服務同名會打架。**保留的風險**：ADR-1 預期未來某個業務會拆成自己的宿主 process，屆時「唯一的 CommandRouter」這個命名會變尷尬（不會有 `CommandRouter2`）。真要拆時再改名，subject 前綴要一起改，成本不小但可控。
- **`InboundBridge` 的 timeout 值與逾時後行為**。目前寫 5 秒，並讓例外往上丟導致連線關閉（client 重連重送）。傾向這樣而不是默默丟掉那則訊息——一則聊天訊息無聲消失比斷線重連糟。要注意 `GatewayWebSocketEndpoint.cs:36` 會把 `OperationCanceledException` 當正常關站吞掉，所以 log 必須記在 bridge 裡（6.2 已這樣寫）。
- **未知 subject 的政策**：傾向記 log + 忽略（前向兼容，讓新版 client 對舊版 CommandRouter 時不會直接斷線），不 terminate。
- **payload 畸形的政策**：傾向視為協定違反直接 terminate。這兩條政策方向不同，需要確認是刻意的。
- **`CommandRouter` 是否 per-command 開 DI scope**：`Gateway/Program.cs:25` 目前 inbound handler 註冊為 singleton；身分層的 `ISessionStore`/`IPresenceDirectory` 也都是 Redis singleton，所以初期不需要 scope。但將來 handler 若要碰 scoped 資源（例如 DbContext），要決定是 per-command 開 scope 還是各 handler 自己用 `IServiceScopeFactory`。
- **限流參數**（per-connection / per-subject）：跟 `connection-layer.md` 第 9 節「Gateway 要不要在 handshake 階段做限流/來源檢查」是同一個安全邊界問題，建議一起決定。
- **handler 例外時要不要回訊息給 client**：ack 會帶 `HANDLER_FAILED` 讓 CommandRouter 記 log 與 metrics，但「client 要不要收到一則錯誤訊息」屬於各命令自己的協定設計，本層不強制。
- **上層目前收不到「連線已斷開」的通知**：`ConnectionLifecycle.OnDisconnectedAsync` 只清 registry 與 directory，沒有任何對外事件。房間層將來一定會需要（斷線要退房），這需要連線層新增一個對外事件，屬於連線層的變更，不在本文件範圍——但要記在案，因為它會影響房間層的設計順序。

## 10. 對既有文件的影響

### `identity-layer.md`（已同步修訂）

- **第 3 節表格**：`identity.bind inbound handler（Gateway 端 DI 插件）` 那一列拆成兩列——`IdentityBindHandler`（註冊在 `CommandRouter` 的 `IPacketHandler<BindRequest>`）與 `IdentityBoundFilter`（註冊在 `CommandRouter` 的 `IInboundFilter`）。取代 `NoOpInboundMessageHandler` 的是協定層的 `InboundBridge`，不是身分層。
- **第 4 節元件關係圖**：新增 `CommandRouter` subgraph，`IB` 從 `Gateway` 移進去，`Gateway` 內改放 `InboundBridge`。
- **第 5.2 節序列圖**：`Gateway` 與 handler 之間插入 `CommandRouter` 這一跳，並在結尾補上 `InboundAck` 與「Gateway 才讀下一個 frame」。
- **第 6.2 節**：`IPresenceDirectory` 新增 `GetBoundUserIdAsync(connectionId)`。
- **第 6.3 節**：`IdentityBindingInboundHandler : IInboundMessageHandler` 連同 `if (subject != "identity.bind") return;` 改寫成 `IPacketHandler<BindRequest>`，`payload.ToStringUtf8()` 換成 typed 欄位，新增 `BindRequest { string session_token = 1; }` 放身分層自己的 proto。原本「啟動時把這個實作換掉 `NoOpInboundMessageHandler` 的 DI 註冊即可」那句已移除。
- **新增第 6.4 節**：`IdentityBoundFilter`，原本的 `POST /login` 順移為 6.5。
- **新增 ADR-8**：ADR-6 的 filter 需要「`ConnectionId` → 是否已綁定」的查詢，而該文件原本只有 `UserId → ConnectionId` 單向，因此新增反向 key `ConnectionUser:{connectionId} → userId`。反向 key 必須設 TTL（正向 key 不設 TTL 的理由不適用——反向 key 每條連線一筆、不會被覆寫），TTL 長度留在該文件第 9 節待決。

### `connection-layer.md` 不需要修改設計

協定層只使用連線層既有的 `IInboundMessageHandler` 插槽、`IOutboundGateway`、`IConnectionTerminator`，沒有要求連線層新增或改變任何東西。本層 ADR-5 與 ADR-6 的前置條件 `IConnectionTerminator`（該文件 ADR-7）已實作完成，`CommandRouter` 只要 `AddConnectionTerminator()` 就能用。
