using Chat.Protos;
using Common.Connections;
using NATS.Client.Core;
using NSubstitute;

namespace Common.Tests.Connections;

public class ConnectionEventPublisherTests
{
	[Fact]
	public async Task PublishDisconnectedAsync_PublishesToTheEventSubject()
	{
		var connection = Substitute.For<INatsConnection>();
		var publisher = new ConnectionEventPublisher(connection);

		await publisher.PublishDisconnectedAsync("conn-1", "node-1", "user-1");

		// 刻意不在 connect.* 家族裡——那個前綴是「投遞給某個 Gateway 節點」的意思。
		// principal 一起帶上，訂閱端才不需要 connectionId -> 身分的反向索引。
		//
		// 用原生 INatsConnection 而不是 Adaptare 的 IMessageSender：訂閱端也是原生訂的，
		// 兩端不對稱時事件根本到不了（見 ConnectionEventPublisher 的註解）。
		await connection.Received(1).PublishAsync(
			"events.connection.disconnected",
			Arg.Is<byte[]>(bytes => Matches(bytes, "conn-1", "node-1", "user-1")),
			Arg.Any<NatsHeaders?>(),
			Arg.Any<string?>(),
			Arg.Any<INatsSerialize<byte[]>?>(),
			Arg.Any<NatsPubOpts?>(),
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
