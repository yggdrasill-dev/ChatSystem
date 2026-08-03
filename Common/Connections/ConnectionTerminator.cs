using Adaptare;
using Chat.Protos;
using Common.Delivery;
using Google.Protobuf;

namespace Common.Connections;

internal sealed class ConnectionTerminator(IMessageSender messageSender) : IConnectionTerminator
{
	private const string DispatchSubject = "dispatch.terminate";

	public async ValueTask TerminateAsync(
		IReadOnlyCollection<string> connectionIds,
		CancellationToken cancellationToken = default)
	{
		// 沿用投遞路徑的分批保護：終止一批連線同樣可能帶上萬個 connectionId，
		// 序列化後會超過 NATS 的 payload 上限。這裡沒有 payload，所以長度傳 0。
		foreach (var batch in DeliveryBatching.Chunk(connectionIds, payloadLength: 0))
		{
			var request = new TerminateRequest();
			request.ConnectionIds.AddRange(batch);

			await messageSender.PublishAsync(DispatchSubject, request.ToByteArray(), cancellationToken).ConfigureAwait(false);
		}
	}
}
