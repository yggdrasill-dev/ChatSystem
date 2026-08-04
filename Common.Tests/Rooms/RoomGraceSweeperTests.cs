using Chat.Protos;
using Common.Identity;
using Common.Protocol;
using Common.Rooms;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Common.Tests.Rooms;

public class RoomGraceSweeperTests
{
	private static readonly DateTimeOffset _Now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

	[Fact]
	public async Task SweepAsync_RemovesExpiredMembers_AndTellsTheRest()
	{
		var context = new SweeperContext();
		context.Membership
			.ListExpiredAsync(Arg.Any<CancellationToken>())
			.Returns([("room-1", "alice")]);
		context.Membership
			.GetMembersAsync("room-1", Arg.Any<CancellationToken>())
			.Returns([new RoomMember("bob", _Now, "conn-bob", null)]);

		await context.Sweeper.SweepAsync(CancellationToken.None);

		await context.Membership.Received(1).RemoveAsync("room-1", "alice", Arg.Any<CancellationToken>());

		var left = Assert.Single(context.Publisher.Sent);
		Assert.Equal("alice", Assert.IsType<RoomMemberLeft>(left.Message).UserId);
		Assert.Equal(["conn-bob"], left.ConnectionIds);
	}

	[Fact]
	public async Task SweepAsync_DoesNothing_WhenNothingExpired()
	{
		var context = new SweeperContext();
		context.Membership.ListExpiredAsync(Arg.Any<CancellationToken>()).Returns([]);

		await context.Sweeper.SweepAsync(CancellationToken.None);

		await context.Membership.DidNotReceive().RemoveAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
		Assert.Empty(context.Publisher.Sent);
	}

	[Fact]
	public async Task SweepAsync_KeepsGoing_WhenOneRoomFails()
	{
		var context = new SweeperContext();
		context.Membership
			.ListExpiredAsync(Arg.Any<CancellationToken>())
			.Returns([("broken", "alice"), ("room-2", "carol")]);
		context.Membership
			.RemoveAsync("broken", "alice", Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("redis is down"));
		context.Membership.GetMembersAsync("room-2", Arg.Any<CancellationToken>()).Returns([]);

		// 一個房間的失敗不該讓其他房間的過期成員也卡住
		await context.Sweeper.SweepAsync(CancellationToken.None);

		await context.Membership.Received(1).RemoveAsync("room-2", "carol", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task SweepAsync_Survives_WhenListingItselfFails()
	{
		var context = new SweeperContext();
		context.Membership
			.ListExpiredAsync(Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("redis is down"));

		// 掃描失敗就等下一輪：過期的成員還在 sorted set 上，這一輪的失敗沒有累積效果
		await context.Sweeper.SweepAsync(CancellationToken.None);
	}

	[Fact]
	public async Task SweepAsync_IsIdempotent_BecauseEveryReplicaRunsItsOwn()
	{
		var context = new SweeperContext();
		context.Membership
			.ListExpiredAsync(Arg.Any<CancellationToken>())
			.Returns([("room-1", "alice")]);
		context.Membership.GetMembersAsync("room-1", Arg.Any<CancellationToken>()).Returns([]);

		await context.Sweeper.SweepAsync(CancellationToken.None);
		await context.Sweeper.SweepAsync(CancellationToken.None);

		// CommandRouter 是多複本，同一個過期成員會被多個 sweeper 處理。重複呼叫 RemoveAsync
		// 必須安全（HDEL/ZREM 天生 idempotent），重複的廣播由 client 容忍。
		await context.Membership.Received(2).RemoveAsync("room-1", "alice", Arg.Any<CancellationToken>());
	}

	private sealed class SweeperContext
	{
		public SweeperContext()
		{
			Presence
				.ResolveConnectionsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
				.Returns(call => (IReadOnlyCollection<string>)
					[.. (call.Arg<IReadOnlyCollection<string>>() ?? []).Select(userId => $"conn-{userId}")]);

			Sweeper = new RoomGraceSweeper(
				Membership,
				new RoomBroadcaster(Presence, Publisher),
				NullLogger<RoomGraceSweeper>.Instance);
		}

		public IRoomMembership Membership { get; } = Substitute.For<IRoomMembership>();

		public IPresenceDirectory Presence { get; } = Substitute.For<IPresenceDirectory>();

		public RecordingPublisher Publisher { get; } = new();

		public RoomGraceSweeper Sweeper { get; }
	}

	private sealed class RecordingPublisher : IPacketPublisher
	{
		public List<(IReadOnlyCollection<string> ConnectionIds, IMessage Message)> Sent { get; } = [];

		public ValueTask PublishAsync<TMessage>(
			IReadOnlyCollection<string> connectionIds,
			TMessage message,
			CancellationToken cancellationToken = default)
			where TMessage : IMessage<TMessage>
		{
			Sent.Add((connectionIds, message));

			return ValueTask.CompletedTask;
		}
	}
}
