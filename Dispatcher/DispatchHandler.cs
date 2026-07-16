using Adaptare;
using Chat.Protos;
using Common.Connections;
using Google.Protobuf;

namespace Dispatcher;

// 訂閱 dispatch.deliver（掛 queue group，多複本互相分攤負載），
// 查 ConnectionDirectory 依 NodeId 分組後，投遞到各 Gateway 節點專屬的 connect.deliver.{nodeId}。
public sealed class DispatchHandler(
	IConnectionDirectory connectionDirectory,
	IMessageSender messageSender,
	ILogger<DispatchHandler> logger) : IMessageHandler<byte[]>
{
	public async ValueTask HandleAsync(
		string subject,
		byte[] data,
		IEnumerable<MessageHeaderValue>? headerValues,
		CancellationToken cancellationToken = default)
	{
		var request = DeliverRequest.Parser.ParseFrom(data);

		var nodesByConnection = await connectionDirectory
			.ResolveNodesAsync(request.ConnectionIds, cancellationToken)
			.ConfigureAwait(false);
		// 查不到的 connectionId（已斷線/過期）直接被省略，這裡不用特別處理

		foreach (var group in nodesByConnection.GroupBy(kv => kv.Value, kv => kv.Key))
		{
			var packet = new DeliverPacket { Subject = request.Subject, Payload = request.Payload };
			packet.ConnectionIds.AddRange(group);

			await messageSender
				.PublishAsync($"connect.deliver.{group.Key}", packet.ToByteArray(), cancellationToken)
				.ConfigureAwait(false);
		}

		logger.LogInformation(
			"Dispatched {Subject} to {NodeCount} node(s) for {TargetCount} connection(s).",
			request.Subject,
			nodesByConnection.Values.Distinct().Count(),
			request.ConnectionIds.Count);
	}
}
