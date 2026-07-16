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
}
