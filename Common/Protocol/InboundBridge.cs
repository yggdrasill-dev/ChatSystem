using Adaptare;
using Chat.Protos;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Common.Protocol;

// 協定層的元件，但寄宿在 Gateway process：取代 NoOpInboundMessageHandler 的 DI 註冊，
// 把連線層交出來的 (connectionId, principal, subject, payload) 送給 CommandRouter。
internal sealed class InboundBridge(
	IMessageSender messageSender,
	ILogger<InboundBridge> logger) : IInboundMessageHandler
{
	internal const string InboundSubject = "command.inbound";

	// 這個 timeout 不是存活偵測——CommandRouter 整個不在時 NATS 的 no-responders 會讓
	// RequestAsync 立刻失敗。它只覆蓋「Router 活著但太慢」，所以要寬鬆到能吸收 GC pause
	// 與 Redis failover 而不誤殺大量活著的連線。理由詳見 protocol-layer.md 第 9 節。
	private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(10);

	public async ValueTask HandleAsync(
		string connectionId,
		string principal,
		string subject,
		ByteString payload,
		CancellationToken cancellationToken = default)
	{
		var packet = new InboundPacket
		{
			ConnectionId = connectionId,
			// 連線層在 handshake 驗到的不透明字串，這裡只轉手；CommandRouter 因此不必查表
			// 就知道這則命令是誰送的。
			Principal = principal,
			Subject = subject,
			Payload = payload
		};

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_Timeout);

		byte[] reply;

		try
		{
			reply = await messageSender
				.RequestAsync<byte[], byte[]>(InboundSubject, packet.ToByteArray(), timeout.Token)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// 逾時。往上丟會讓連線關閉（client 重連重送），這是刻意的：只記 log 然後繼續讀
			// 下一個 frame 的話，這則訊息可能稍後才被 Router 處理，單連線順序保證就破了。
			// 注意 GatewayWebSocketEndpoint 會把 OperationCanceledException 當正常關站吞掉，
			// 所以 log 一定要記在這裡。
			logger.LogError(
				"{ConnectionId} {Subject} timed out after {Timeout} waiting for the command router.",
				connectionId,
				subject,
				_Timeout);

			throw;
		}

		var ack = InboundAck.Parser.ParseFrom(reply);

		if (ack.Status != InboundAck.Types.Status.Ok)
			logger.LogWarning(
				"{ConnectionId} {Subject} was not processed: {Status} {Detail}",
				connectionId,
				subject,
				ack.Status,
				ack.Detail);
	}
}
