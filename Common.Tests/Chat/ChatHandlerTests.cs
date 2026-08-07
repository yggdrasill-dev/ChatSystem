using Chat.Protos;
using Common.Chat;
using Common.Chat.Handlers;
using Common.Identity;
using Common.Protocol;
using Common.Rooms;
using Google.Protobuf;
using NSubstitute;
// 這個檔案同時用到領域與 wire 兩個 ChatMessage，所以兩邊都給明確的名字：簡單名稱指領域
// 型別（alias 的優先權高於 using 命名空間），wire 型別叫 MessagePacket。理由見
// Common/Chat/Handlers/ChatPacket.cs。
using ChatMessage = Common.Chat.ChatMessage;
using MessagePacket = Chat.Protos.ChatMessage;
using Status = Chat.Protos.ChatOperationReply.Types.Status;

namespace Common.Tests.Chat;

public class ChatHandlerTests
{
	private static readonly DateTimeOffset _Now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
	private static readonly CommandContext _Alice = new("conn-alice", "alice");

	[Fact]
	public async Task Send_RepliesNotInRoom_WhenTheUserIsNowhere()
	{
		var harness = new Harness();

		await harness.SendHandler().HandleAsync(_Alice, new SendChatMessageRequest { Body = "hi" });

		// 「你在哪間房」這一次查詢同時是授權、定址、與「房間還在嗎」（ADR-5、ADR-10）。
		Assert.Equal(Status.NotInRoom, harness.Single<ChatOperationReply>().Status);
		Assert.Empty(harness.Sent.Where(sent => sent.Message is MessagePacket));
	}

	[Theory]
	[InlineData("", Status.BodyEmpty)]
	[InlineData("   ", Status.BodyEmpty)]
	public async Task Send_RejectsAnEmptyBody_WithoutTouchingAnyStore(string body, Status expected)
	{
		var harness = new Harness();
		harness.StubRoom("alice", "room-1");

		await harness.SendHandler().HandleAsync(_Alice, new SendChatMessageRequest { Body = body });

		Assert.Equal(expected, harness.Single<ChatOperationReply>().Status);

		// 內容檢查在最前面，所以連 IRoomMembership 都不該被碰到（沒有 I/O）。
		await harness.Membership
			.DidNotReceive()
			.GetCurrentRoomAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Send_RejectsAnOverlongBody()
	{
		var harness = new Harness();

		var body = new string('x', ChatMessageDraft.MaxBodyLength + 1);

		await harness.SendHandler().HandleAsync(_Alice, new SendChatMessageRequest { Body = body });

		Assert.Equal(Status.BodyTooLong, harness.Single<ChatOperationReply>().Status);
	}

	[Fact]
	public async Task Send_StoresBeforeBroadcasting()
	{
		var harness = new Harness();
		harness.StubRoom("alice", "room-1");
		harness.StubMembers("room-1", "alice", "bob");

		await harness.SendHandler().HandleAsync(_Alice, new SendChatMessageRequest { Body = "hi" });

		// ADR-1：先存後廣播。歷史記錄是真相來源，所以廣播出去的每一則都一定在 store 裡。
		var broadcast = harness.Single<MessagePacket>();
		var stored = Assert.Single((await harness.Store.GetPageAsync("room-1", 0, 10)).Messages);

		Assert.Equal(stored.OrderKey, broadcast.OrderKey);
		Assert.Equal("hi", stored.Body);

		// 送出者自己也在名單上——那正是他拿到 order_key 的方式，所以成功路徑不回 OK。
		Assert.Equal(["conn-alice", "conn-bob"], harness.TargetsOf<MessagePacket>());
		Assert.Empty(harness.Sent.Where(sent => sent.Message is ChatOperationReply));
	}

	[Fact]
	public async Task Send_EmbedsTheDisplayNameSnapshot_NotTheCurrentOne()
	{
		var harness = new Harness();
		harness.StubRoom("alice", "room-1");
		harness.StubMembers("room-1", "alice");
		harness.StubDisplayName("alice", "Alice");

		await harness.SendHandler().HandleAsync(_Alice, new SendChatMessageRequest { Body = "before" });

		// 改名。ADR-3 的語意是「聊天記錄呈現他當時叫什麼」，所以舊訊息不該被改寫。
		harness.StubDisplayName("alice", "Alicia");
		await harness.SendHandler().HandleAsync(_Alice, new SendChatMessageRequest { Body = "after" });

		var page = await harness.Store.GetPageAsync("room-1", 0, 10);

		Assert.Equal(
			[("after", "Alicia"), ("before", "Alice")],
			page.Messages.Select(message => (message.Body, message.SenderDisplayName)));
	}

	[Fact]
	public async Task Send_LeavesTheSnapshotEmpty_WhenTheUserHasNoProfile()
	{
		var harness = new Harness();
		harness.StubRoom("alice", "room-1");
		harness.StubMembers("room-1", "alice");

		// GetDisplayNameAsync 回 null（沒登入過／Google 沒給 name）不該讓命令失敗——
		// 顯示什麼由 client 決定。
		await harness.SendHandler().HandleAsync(_Alice, new SendChatMessageRequest { Body = "hi" });

		Assert.Equal(string.Empty, harness.Single<MessagePacket>().SenderDisplayName);
	}

	[Fact]
	public async Task Send_RepliesRateLimited_AndStoresNothing()
	{
		var harness = new Harness(new ChatRateLimit(MessagesPerSecond: 1));
		harness.StubRoom("alice", "room-1");
		harness.StubMembers("room-1", "alice");

		await harness.SendHandler().HandleAsync(_Alice, new SendChatMessageRequest { Body = "first" });
		harness.Clear();

		await harness.SendHandler().HandleAsync(_Alice, new SendChatMessageRequest { Body = "second" });

		// 限流必須用明確的下行訊息回覆，不能沉默——那正是它放 handler 而不放 IInboundFilter
		// 的理由（ADR-7）。
		Assert.Equal(Status.RateLimited, harness.Single<ChatOperationReply>().Status);
		Assert.Equal("room-1", harness.Single<ChatOperationReply>().RoomId);

		// 被擋掉的訊息不該進 store：限流的整個目的就是保護那個無上限成長的儲存。
		var page = await harness.Store.GetPageAsync("room-1", 0, 10);

		Assert.Equal(["first"], page.Messages.Select(message => message.Body));
	}

	[Fact]
	public async Task Send_ResequencesAndRetries_WhenTheOrderKeyCollides()
	{
		var harness = new Harness();
		harness.StubRoom("alice", "room-1");
		harness.StubMembers("room-1", "alice");

		// 先把 sequencer 即將發出的那個號佔掉，逼出 TryAppendAsync 回 false 的那條路徑。
		// 真的碰撞要同一間房、同一微秒、兩個 process 同時寫入，測試沒辦法自然製造。
		var taken = harness.Sequencer.Peek();

		Assert.True(await harness.Store.TryAppendAsync(
			new ChatMessage("room-1", taken, "someone-else", string.Empty, "squatter", _Now)));

		await harness.SendHandler().HandleAsync(_Alice, new SendChatMessageRequest { Body = "hi" });

		// 重新發號再試一次就成功了——寫入是單一插入、沒有交易，所以沒有回滾要處理（6.4）。
		var broadcast = harness.Single<MessagePacket>();

		Assert.NotEqual(taken, broadcast.OrderKey);
		Assert.Equal("hi", broadcast.Body);
	}

	[Fact]
	public async Task Send_Throws_WhenTheStoreKeepsRejectingTheAppend()
	{
		var harness = new Harness(store: new AlwaysFullStore());
		harness.StubRoom("alice", "room-1");

		// 內部故障不走 ChatOperationReply：那個型別留給「使用者做錯了什麼」。協定層會記
		// error 並回 HANDLER_FAILED（protocol-layer.md ADR-8 劃的界線）。
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			harness.SendHandler().HandleAsync(_Alice, new SendChatMessageRequest { Body = "hi" }).AsTask());

		Assert.Empty(harness.Sent);
	}

	[Fact]
	public async Task History_RepliesNotInRoom_WhenTheUserIsNowhere()
	{
		var harness = new Harness();

		await harness.HistoryHandler().HandleAsync(_Alice, new ChatHistoryRequest());

		// **沒有辦法查詢自己不在的房間的歷史**——命令不帶 room_id，所以這個限制是結構性的
		// （ADR-5）。這也正是關掉的房間查不到歷史的原因，而 ADR-10 的答案是那些資料不該留。
		Assert.Equal(Status.NotInRoom, harness.Single<ChatOperationReply>().Status);
	}

	[Fact]
	public async Task History_ReturnsTheNewestPage_AndOnlyToTheAskingConnection()
	{
		var harness = new Harness();
		harness.StubRoom("alice", "room-1");
		await harness.SeedAsync("room-1", "one", "two", "three");

		await harness.HistoryHandler().HandleAsync(_Alice, new ChatHistoryRequest { Limit = 2 });

		var history = harness.Single<ChatHistory>();

		Assert.Equal("room-1", history.RoomId);
		Assert.Equal(["three", "two"], history.Messages.Select(message => message.Body));
		Assert.True(history.HasMore);

		// 回覆直接送回 context.ConnectionId，不查 Presence（Supersede 之後可能指向別條連線）。
		Assert.Equal(["conn-alice"], harness.TargetsOf<ChatHistory>());
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(ChatHistoryHandler.MaxLimit + 1)]
	public async Task History_ClampsABadLimit_InsteadOfReplyingAnError(int limit)
	{
		var harness = new Harness();
		harness.StubRoom("alice", "room-1");
		await harness.SeedAsync("room-1", "one", "two");

		await harness.HistoryHandler().HandleAsync(_Alice, new ChatHistoryRequest { Limit = limit });

		// 分頁參數壞掉是 client 的 bug；讓它拿到一頁合理的資料比回一個錯誤碼有用（6.2）。
		Assert.Equal(2, harness.Single<ChatHistory>().Messages.Count);
	}

	[Fact]
	public async Task History_TreatsANegativeCursor_AsFromTheNewest()
	{
		var harness = new Harness();
		harness.StubRoom("alice", "room-1");
		await harness.SeedAsync("room-1", "one", "two");

		await harness.HistoryHandler().HandleAsync(_Alice, new ChatHistoryRequest { BeforeOrderKey = -5 });

		// 不夾的話 `order_key < -5` 會回空頁，看起來像「沒有歷史訊息」。
		Assert.Equal(["two", "one"], harness.Single<ChatHistory>().Messages.Select(message => message.Body));
	}

	private sealed class Harness
	{
		public Harness(ChatRateLimit? rateLimit = null, IChatMessageStore? store = null)
		{
			Presence
				.ResolveConnectionsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
				.Returns(call => (IReadOnlyCollection<string>)
					[.. (call.Arg<IReadOnlyCollection<string>>() ?? []).Select(userId => $"conn-{userId}")]);

			Store = store ?? new InMemoryChatMessageStore();
			Broadcaster = new RoomBroadcaster(Presence, Publisher);
			RateLimiter = new InMemoryChatRateLimiter(Time, rateLimit ?? ChatRateLimit.Default);
		}

		public IRoomMembership Membership { get; } = Substitute.For<IRoomMembership>();

		public IUserProfileStore Profiles { get; } = Substitute.For<IUserProfileStore>();

		public IPresenceDirectory Presence { get; } = Substitute.For<IPresenceDirectory>();

		public IChatMessageStore Store { get; }

		public PeekableSequencer Sequencer { get; } = new(_Now);

		public IChatRateLimiter RateLimiter { get; }

		public RecordingPublisher Publisher { get; } = new();

		public RoomBroadcaster Broadcaster { get; }

		public TimeProvider Time { get; } = new FixedTimeProvider(_Now);

		public List<(IReadOnlyCollection<string> ConnectionIds, IMessage Message)> Sent => Publisher.Sent;

		public ChatSendHandler SendHandler() =>
			new(Membership, Profiles, Sequencer, Store, RateLimiter, Broadcaster, Time);

		public ChatHistoryHandler HistoryHandler() => new(Membership, Store, Broadcaster);

		public void StubRoom(string userId, string roomId) =>
			Membership.GetCurrentRoomAsync(userId, Arg.Any<CancellationToken>()).Returns(roomId);

		public void StubMembers(string roomId, params string[] userIds) =>
			Membership
				.GetMembersAsync(roomId, Arg.Any<CancellationToken>())
				.Returns([.. userIds.Select(userId => new RoomMember(userId, _Now, $"conn-{userId}", null))]);

		public void StubDisplayName(string userId, string displayName) =>
			Profiles.GetDisplayNameAsync(userId, Arg.Any<CancellationToken>()).Returns(displayName);

		// 直接寫進 store，不經過 handler：歷史查詢的測試不該依賴送出路徑是對的。
		public async Task SeedAsync(string roomId, params string[] bodies)
		{
			foreach (var body in bodies)
				Assert.True(await Store.TryAppendAsync(
					new ChatMessage(roomId, Sequencer.Next(), "alice", "Alice", body, _Now)));
		}

		public void Clear() => Sent.Clear();

		public TMessage Single<TMessage>() where TMessage : IMessage =>
			Assert.Single(Sent.Select(sent => sent.Message).OfType<TMessage>());

		public IReadOnlyCollection<string> TargetsOf<TMessage>() where TMessage : IMessage =>
			[.. Sent.Where(sent => sent.Message is TMessage).SelectMany(sent => sent.ConnectionIds).Order()];
	}

	// 跟正式的 sequencer 同樣的形狀（單調、唯一、稀疏無所謂），但可以先問「下一個是什麼」
	// ——碰撞那條測試需要事先把那個號佔掉。
	private sealed class PeekableSequencer(DateTimeOffset start) : IMessageSequencer
	{
		private long m_Next = start.ToUnixTimeMilliseconds() * 1000L;

		public long Peek() => m_Next;

		public long Next() => m_Next++;
	}

	private sealed class AlwaysFullStore : IChatMessageStore
	{
		public ValueTask<bool> TryAppendAsync(ChatMessage message, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(false);

		public ValueTask<ChatMessagePage> GetPageAsync(
			string roomId,
			long beforeOrderKey,
			int limit,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(new ChatMessagePage([], false));

		public ValueTask<int> DeleteOlderThanAsync(
			DateTimeOffset cutoff,
			int batchSize,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(0);
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

	private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}
}
