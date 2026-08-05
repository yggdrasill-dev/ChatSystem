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

		await publisher.PublishDisconnectedAsync("conn-1", "node-1", "user-1");

		// 刻意不在 connect.* 家族裡——那個前綴是「投遞給某個 Gateway 節點」的意思。
		// principal 一起帶上，訂閱端才不需要 connectionId -> 身分的反向索引。
		await sender.Received(1).PublishAsync(
			"events.connection.disconnected",
			Arg.Is<byte[]>(bytes => Matches(bytes, "conn-1", "node-1", "user-1")),
			Arg.Any<IEnumerable<MessageHeaderValue>>(),
			Arg.Any<CancellationToken>());
	}

	private static bool Matches(byte[]? bytes, string connectionId, string nodeId, string principal)
	{
		var message = ConnectionDisconnected.Parser.ParseFrom(bytes);

		return message.ConnectionId == connectionId
			&& message.NodeId == nodeId
			&& message.Principal == principal;
	}
}
