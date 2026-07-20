using Adaptare;
using Chat.Protos;
using Common.Connections;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dispatcher.Tests;

public class DispatchHandlerTests
{
	[Fact]
	public async Task HandleAsync_GroupsConnectionsByNodeId_AndPublishesOnePacketPerNode()
	{
		var directory = Substitute.For<IConnectionDirectory>();
		directory
			.ResolveNodesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
			.Returns(new Dictionary<string, string>
			{
				["conn-a"] = "node-1",
				["conn-b"] = "node-1",
				["conn-c"] = "node-2",
			});

		var sender = Substitute.For<IMessageSender>();
		var handler = new DispatchHandler(directory, sender, NullLogger<DispatchHandler>.Instance);

		var request = new DeliverRequest { Subject = "chat.receive", Payload = ByteString.CopyFromUtf8("hi") };
		request.ConnectionIds.AddRange(["conn-a", "conn-b", "conn-c"]);

		await handler.HandleAsync("dispatch.deliver", request.ToByteArray(), null);

		await sender.Received(1).PublishAsync(
			"connect.deliver.node-1",
			Arg.Is<byte[]>(bytes => HasConnectionIds(bytes, "conn-a", "conn-b")),
			Arg.Any<IEnumerable<MessageHeaderValue>>(),
			Arg.Any<CancellationToken>());

		await sender.Received(1).PublishAsync(
			"connect.deliver.node-2",
			Arg.Is<byte[]>(bytes => HasConnectionIds(bytes, "conn-c")),
			Arg.Any<IEnumerable<MessageHeaderValue>>(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_SkipsMissingConnections_WithoutPublishingOrThrowing()
	{
		var directory = Substitute.For<IConnectionDirectory>();
		directory
			.ResolveNodesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
			.Returns(new Dictionary<string, string>()); // 全部查不到（已斷線/過期）

		var sender = Substitute.For<IMessageSender>();
		var handler = new DispatchHandler(directory, sender, NullLogger<DispatchHandler>.Instance);

		var request = new DeliverRequest { Subject = "chat.receive", Payload = ByteString.CopyFromUtf8("hi") };
		request.ConnectionIds.Add("gone");

		await handler.HandleAsync("dispatch.deliver", request.ToByteArray(), null);

		await sender.DidNotReceive().PublishAsync(
			Arg.Any<string>(),
			Arg.Any<byte[]>(),
			Arg.Any<IEnumerable<MessageHeaderValue>>(),
			Arg.Any<CancellationToken>());
	}

	private static bool HasConnectionIds(byte[]? bytes, params string[] expectedConnectionIds)
	{
		var packet = DeliverPacket.Parser.ParseFrom(bytes);
		return packet.Subject == "chat.receive"
			&& packet.Payload.ToStringUtf8() == "hi"
			&& packet.ConnectionIds.OrderBy(id => id).SequenceEqual(expectedConnectionIds.OrderBy(id => id));
	}

	[Fact]
	public async Task HandleAsync_SplitsSingleNodeGroupIntoMultiplePackets_WhenBatchWouldExceedSizeBudget()
	{
		var connectionIds = Enumerable.Range(0, 1_200).Select(i => $"conn-{i}").ToArray();

		var directory = Substitute.For<IConnectionDirectory>();
		directory
			.ResolveNodesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
			.Returns(connectionIds.ToDictionary(id => id, _ => "node-1"));

		var sender = Substitute.For<IMessageSender>();
		var handler = new DispatchHandler(directory, sender, NullLogger<DispatchHandler>.Instance);

		// payload 刻意調大，讓 DeliveryBatching 算出的批次上限縮小到方便測試的數量
		var payload = ByteString.CopyFrom(new byte[880_000]);
		var request = new DeliverRequest { Subject = "chat.receive", Payload = payload };
		request.ConnectionIds.AddRange(connectionIds);

		var publishedPacketIds = new List<string[]>();
		sender
			.PublishAsync(
				"connect.deliver.node-1",
				Arg.Do<byte[]>(bytes => publishedPacketIds.Add(DeliverPacket.Parser.ParseFrom(bytes).ConnectionIds.ToArray())),
				Arg.Any<IEnumerable<MessageHeaderValue>>(),
				Arg.Any<CancellationToken>())
			.Returns(ValueTask.CompletedTask);

		await handler.HandleAsync("dispatch.deliver", request.ToByteArray(), null);

		Assert.True(publishedPacketIds.Count > 1, "Expected node-1's large connection batch to be split across multiple packets.");
		Assert.Equal(connectionIds.OrderBy(id => id), publishedPacketIds.SelectMany(ids => ids).OrderBy(id => id));
	}
}
