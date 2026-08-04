using Adaptare;
using Chat.Protos;
using Common.Connections;
using Common.Protocol;
using Google.Protobuf;

namespace CommandRouter;

// 訂閱 command.inbound（掛 queue group）。整個系統唯一解析 client 命令內容的地方。
//
// 用 IMessageProcessor（request/reply）而不是 IMessageHandler：Gateway 要等 ack 回來才讀
// 下一個 frame，這正是單一連線訊息順序的保證來源（見 protocol-layer.md ADR-2）。
public sealed class InboundProcessor(
	IServiceScopeFactory scopeFactory,
	PacketRegistry registry,
	IConnectionTerminator connectionTerminator,
	ILogger<InboundProcessor> logger) : IMessageProcessor<byte[], byte[]>
{
	public async ValueTask<byte[]> HandleAsync(
		string subject,
		byte[] data,
		IEnumerable<MessageHeaderValue>? headerValues,
		CancellationToken cancellationToken = default)
	{
		// 這一層解不開代表 Gateway 端送了非 InboundPacket 的東西，是內部錯誤不是 client 的錯，
		// 刻意讓它往上丟：吞掉只會變成沉默的資料遺失。
		var packet = InboundPacket.Parser.ParseFrom(data);

		// principal 隨封包一起到，不查表。Gateway 已經在 handshake 驗過身分，這裡無條件相信
		// 它——前提是 command.inbound 只有 Gateway 能 publish。
		var context = new CommandContext(packet.ConnectionId, packet.Principal);

		// 一則命令 = 一個 scope，對應 ASP.NET Core 的 per-request 心智模型（見第 9 節決議）。
		using var scope = scopeFactory.CreateScope();

		var filters = scope.ServiceProvider
			.GetServices<IInboundFilter>()
			.OrderBy(filter => filter.Order);

		foreach (var filter in filters)
		{
			var decision = await filter
				.EvaluateAsync(context, packet.Subject, cancellationToken)
				.ConfigureAwait(false);

			if (decision == FilterDecision.Allow)
				continue;

			return await RejectAsync(filter, decision, packet, cancellationToken).ConfigureAwait(false);
		}

		if (!registry.IsInboundSubject(packet.Subject))
		{
			// 不關連線：rolling deploy 期間必然出現「新 client + 舊 CommandRouter」（ADR-8）。
			// 記 warning 而非 debug，因為這同時是「版本錯位」與「有人在亂送」的訊號。
			logger.LogWarning(
				"{ConnectionId} sent unknown subject {Subject}, ignoring.",
				packet.ConnectionId,
				packet.Subject);

			return Ack(InboundAck.Types.Status.UnknownSubject, packet.Subject);
		}

		try
		{
			await registry
				.DispatchAsync(scope.ServiceProvider, packet.Subject, context, packet.Payload, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (InvalidProtocolBufferException ex)
		{
			// 同樣不關連線：client 幾乎都會自動重連，terminate 會變成「重連→送同一個壞封包→
			// 又被踢」的緊迫迴圈，成本比忽略這一則更高（ADR-8）。
			logger.LogWarning(
				ex,
				"{ConnectionId} sent a malformed payload for {Subject}, ignoring.",
				packet.ConnectionId,
				packet.Subject);

			return Ack(InboundAck.Types.Status.MalformedPayload, packet.Subject);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// handler 的 bug 不該關掉使用者的連線。
			logger.LogError(ex, "Handler for {Subject} failed for {ConnectionId}.", packet.Subject, packet.ConnectionId);

			return Ack(InboundAck.Types.Status.HandlerFailed, ex.GetType().Name);
		}

		return Ack(InboundAck.Types.Status.Ok);
	}

	private async ValueTask<byte[]> RejectAsync(
		IInboundFilter filter,
		FilterDecision decision,
		InboundPacket packet,
		CancellationToken cancellationToken)
	{
		var filterName = filter.GetType().Name;

		if (decision == FilterDecision.Terminate)
		{
			logger.LogWarning(
				"{ConnectionId} {Subject} rejected by {Filter}, terminating the connection.",
				packet.ConnectionId,
				packet.Subject,
				filterName);

			// 處置動作由這裡統一執行，filter 只負責判斷（ADR-6）。
			await connectionTerminator
				.TerminateAsync([packet.ConnectionId], cancellationToken)
				.ConfigureAwait(false);
		}
		else
		{
			logger.LogWarning(
				"{ConnectionId} {Subject} dropped by {Filter}.",
				packet.ConnectionId,
				packet.Subject,
				filterName);
		}

		return Ack(InboundAck.Types.Status.RejectedByFilter, filterName);
	}

	private static byte[] Ack(InboundAck.Types.Status status, string detail = "") =>
		new InboundAck { Status = status, Detail = detail }.ToByteArray();
}
