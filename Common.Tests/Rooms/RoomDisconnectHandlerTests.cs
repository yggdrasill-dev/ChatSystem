using Chat.Protos;
using Common.Rooms;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Common.Tests.Rooms;

public class RoomDisconnectHandlerTests
{
	[Fact]
	public async Task HandleAsync_MarksTheMemberDisconnected_UsingThePrincipalFromTheEvent()
	{
		var (handler, membership) = Create();

		await handler.HandleAsync(
			RoomDisconnectHandler.Subject,
			new ConnectionDisconnected { ConnectionId = "conn-1", NodeId = "node-1", Principal = "alice" }.ToByteArray(),
			null);

		// 事件帶 principal，所以不需要 connectionId → userId 的反查；connectionId 仍要傳下去，
		// 標記本身要靠它做 fencing
		await membership.Received(1).MarkDisconnectedAsync("alice", "conn-1", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_IgnoresEventsWithoutAPrincipal()
	{
		var (handler, membership) = Create();

		await handler.HandleAsync(
			RoomDisconnectHandler.Subject,
			new ConnectionDisconnected { ConnectionId = "conn-1", NodeId = "node-1" }.ToByteArray(),
			null);

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
		var (handler, membership) = Create();

		await handler.HandleAsync(RoomDisconnectHandler.Subject, length is null ? null! : [], null);

		await membership.DidNotReceive().MarkDisconnectedAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_SwallowsFailures_SoOneEventCannotKillTheSubscription()
	{
		var (handler, membership) = Create();
		membership
			.MarkDisconnectedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("redis is down"));

		// 事件是 best-effort，而寬限期的正確性靠「讀取時過濾」而不是靠這個訂閱（ADR-2）
		await handler.HandleAsync(
			RoomDisconnectHandler.Subject,
			new ConnectionDisconnected { ConnectionId = "conn-1", Principal = "alice" }.ToByteArray(),
			null);
	}

	private static (RoomDisconnectHandler Handler, IRoomMembership Membership) Create()
	{
		var membership = Substitute.For<IRoomMembership>();

		return (new RoomDisconnectHandler(membership, NullLogger<RoomDisconnectHandler>.Instance), membership);
	}
}
