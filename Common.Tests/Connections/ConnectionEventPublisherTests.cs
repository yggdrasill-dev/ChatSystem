using Adaptare;
using Chat.Protos;
using Common.Connections;
using NSubstitute;

namespace Common.Tests.Connections;

public class ConnectionEventPublisherTests
{
	[Fact]
	public async Task PublishDisconnectedAsync_PublishesToTheEventSubject()
	{
		var sender = Substitute.For<IMessageSender>();
		var publisher = new ConnectionEventPublisher(sender);

		await publisher.PublishDisconnectedAsync("conn-1", "node-1");

		// 刻意不在 connect.* 家族裡——那個前綴是「投遞給某個 Gateway 節點」的意思
		await sender.Received(1).PublishAsync(
			"events.connection.disconnected",
			Arg.Is<byte[]>(bytes => Matches(bytes, "conn-1", "node-1")),
			Arg.Any<IEnumerable<MessageHeaderValue>>(),
			Arg.Any<CancellationToken>());
	}

	private static bool Matches(byte[]? bytes, string connectionId, string nodeId)
	{
		var message = ConnectionDisconnected.Parser.ParseFrom(bytes);

		return message.ConnectionId == connectionId && message.NodeId == nodeId;
	}
}
