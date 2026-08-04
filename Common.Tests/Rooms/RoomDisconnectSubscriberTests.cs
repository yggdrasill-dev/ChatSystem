using Chat.Protos;
using Common.Rooms;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Common.Tests.Rooms;

public class RoomDisconnectSubscriberTests
{
	[Fact]
	public async Task HandleAsync_MarksTheMemberDisconnected_UsingThePrincipalFromTheEvent()
	{
		var (subscriber, membership) = Create();

		await subscriber.HandleAsync(
			new ConnectionDisconnected { ConnectionId = "conn-1", NodeId = "node-1", Principal = "alice" }.ToByteArray(),
			CancellationToken.None);

		// 事件帶 principal，所以不需要 connectionId → userId 的反查；connectionId 仍要傳下去，
		// 標記本身要靠它做 fencing
		await membership.Received(1).MarkDisconnectedAsync("alice", "conn-1", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_IgnoresEventsWithoutAPrincipal()
	{
		var (subscriber, membership) = Create();

		await subscriber.HandleAsync(
			new ConnectionDisconnected { ConnectionId = "conn-1", NodeId = "node-1" }.ToByteArray(),
			CancellationToken.None);

		// 沒有 principal 的連線不該存在（handshake 一定驗過），但事件是跨 process 來的，
		// 寧可忽略也不要在 Redis 上寫出一個空 userId 的成員
		await membership.DidNotReceive().MarkDisconnectedAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Theory]
	[InlineData(null)]
	[InlineData(0)]
	public async Task HandleAsync_IgnoresEmptyPayloads(int? length)
	{
		var (subscriber, membership) = Create();

		await subscriber.HandleAsync(length is null ? null : [], CancellationToken.None);

		await membership.DidNotReceive().MarkDisconnectedAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_SwallowsFailures_SoOneEventCannotKillTheSubscription()
	{
		var (subscriber, membership) = Create();
		membership
			.MarkDisconnectedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("redis is down"));

		// 事件是 best-effort，而寬限期的正確性靠「讀取時過濾」而不是靠這個訂閱（ADR-2）
		await subscriber.HandleAsync(
			new ConnectionDisconnected { ConnectionId = "conn-1", Principal = "alice" }.ToByteArray(),
			CancellationToken.None);
	}

	private static (RoomDisconnectSubscriber Subscriber, IRoomMembership Membership) Create()
	{
		var membership = Substitute.For<IRoomMembership>();

		return (
			new RoomDisconnectSubscriber(
				Substitute.For<INatsConnection>(),
				membership,
				NullLogger<RoomDisconnectSubscriber>.Instance),
			membership);
	}
}
