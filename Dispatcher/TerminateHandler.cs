using Adaptare;
using Chat.Protos;
using Common.Connections;
using Common.Delivery;
using Google.Protobuf;

namespace Dispatcher;

// 訂閱 dispatch.terminate（掛 queue group，多複本互相分攤負載），
// 查 ConnectionDirectory 依 NodeId 分組後，投遞到各 Gateway 節點專屬的 connect.terminate.{nodeId}。
// 結構跟 DispatchHandler 完全一樣，只是把 DeliverPacket 換成 TerminatePacket。
public sealed class TerminateHandler(
	IConnectionDirectory connectionDirectory,
	IMessageSender messageSender,
	ILogger<TerminateHandler> logger) : IMessageHandler<byte[]>
{
	public async ValueTask HandleAsync(
		string subject,
		byte[] data,
		IEnumerable<MessageHeaderValue>? headerValues,
		CancellationToken cancellationToken = default)
	{
		var request = TerminateRequest.Parser.ParseFrom(data);

		var nodesByConnection = await connectionDirectory
			.ResolveNodesAsync(request.ConnectionIds, cancellationToken)
			.ConfigureAwait(false);
		// 查不到的 connectionId（連線已經自然斷開）直接被省略，呼叫端不需要處理這種情況

		foreach (var group in nodesByConnection.GroupBy(kv => kv.Value, kv => kv.Key))
		{
			var groupedConnectionIds = group.ToArray();

			foreach (var batch in DeliveryBatching.Chunk(groupedConnectionIds, payloadLength: 0))
			{
				var packet = new TerminatePacket();
				packet.ConnectionIds.AddRange(batch);

				await messageSender
					.PublishAsync($"connect.terminate.{group.Key}", packet.ToByteArray(), cancellationToken)
					.ConfigureAwait(false);
			}
		}

		logger.LogInformation(
			"Terminating {TargetCount} connection(s) across {NodeCount} node(s).",
			request.ConnectionIds.Count,
			nodesByConnection.Values.Distinct().Count());
	}
}
