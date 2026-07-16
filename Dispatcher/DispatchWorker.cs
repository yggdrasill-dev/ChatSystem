using Chat.Protos;
using Common.Connections;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Dispatcher;

// 訂閱 dispatch.deliver（掛 queue group，多複本互相分攤負載），
// 查 ConnectionDirectory 依 NodeId 分組後，投遞到各 Gateway 節點專屬的 connect.deliver.{nodeId}。
// 見 docs/architecture/connection-layer.md 第 6.4 節。
public sealed class DispatchWorker(
	INatsConnection connection,
	IConnectionDirectory connectionDirectory,
	ILogger<DispatchWorker> logger) : BackgroundService
{
	private const string DispatchSubject = "dispatch.deliver";

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.LogInformation("Subscribing to {Subject} (queue group: {Group}).", DispatchSubject, DispatchSubject);

		await foreach (var msg in connection.SubscribeAsync<byte[]>(
			DispatchSubject,
			queueGroup: DispatchSubject,
			cancellationToken: stoppingToken))
		{
			if (msg.Data is null)
				continue;

			try
			{
				await HandleAsync(msg.Data, stoppingToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to handle {Subject}.", DispatchSubject);
			}
		}
	}

	private async Task HandleAsync(byte[] data, CancellationToken cancellationToken)
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

			await connection
				.PublishAsync($"connect.deliver.{group.Key}", packet.ToByteArray(), cancellationToken: cancellationToken)
				.ConfigureAwait(false);
		}

		logger.LogInformation(
			"Dispatched {Subject} to {NodeCount} node(s) for {TargetCount} connection(s).",
			request.Subject,
			nodesByConnection.Values.Distinct().Count(),
			request.ConnectionIds.Count);
	}
}
