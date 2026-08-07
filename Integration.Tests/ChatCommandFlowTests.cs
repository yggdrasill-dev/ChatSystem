using Chat.Protos;
using Common.Chat;
using MessagePacket = Chat.Protos.ChatMessage;
using Status = Chat.Protos.ChatOperationReply.Types.Status;

namespace Integration.Tests;

// 聊天層的命令流程，跟房間層共用同一個 harness（CommandFlowHost）。
//
// 這裡的重點是**層與層的接縫**：聊天層自己不做 fan-out，它向房間層要成員名單、重用
// RoomBroadcaster、再走協定層的出口（chat-layer.md 6.8）。單元測試把那整條都 mock 掉了。
//
// 階段 A 特有的一件好事：`AddChatStore()` 註冊的本來就是 in-memory 的 store，所以這條路徑上
// **聊天層是完整的正式註冊**，沒有任何替身——初稿那版的核心性質是 Postgres 交易的性質，
// 在這裡怎麼寫都會通過，等於在測自己寫的替身（§9）。append-only 讓那個問題消失。
public class ChatCommandFlowTests
{
	[Fact]
	public async Task Send_ReachesEveryMemberIncludingTheSender()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomWithMembersAsync("alice", "bob");

		await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = "hello" });

		var message = host.Single<MessagePacket>();

		Assert.Equal(roomId, message.RoomId);
		Assert.Equal("alice", message.SenderUserId);
		Assert.Equal("hello", message.Body);

		// 送出者自己也在名單上——那正是他拿到 order_key 的方式，所以成功路徑不回 OK。
		Assert.Equal(["conn-alice", "conn-bob"], host.TargetsOf<MessagePacket>());
		Assert.Empty(host.All<ChatOperationReply>());
	}

	[Fact]
	public async Task Send_DoesNotReachMembersOfOtherRooms()
	{
		await using var host = await CommandFlowHost.StartAsync();

		await host.CreateRoomWithMembersAsync("alice", "bob");

		// carol 在另一間房。「一次只能在一間房」是產品決定，所以 fan-out 的名單天生是隔離的。
		var other = await host.CreateRoomAsync("carol", "Elsewhere", string.Empty);
		await host.SendAsync("carol", "room.join", new JoinRoomRequest { RoomId = other });
		host.Clear();

		await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = "hello" });

		Assert.Equal(["conn-alice", "conn-bob"], host.TargetsOf<MessagePacket>());
	}

	[Fact]
	public async Task Send_RepliesNotInRoom_WhenTheSenderHasNotJoined()
	{
		await using var host = await CommandFlowHost.StartAsync();
		await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		// 建房**不會**順手加入（room-layer.md §9），所以 alice 這時還不在任何房間裡。
		await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = "hello" });

		Assert.Equal(Status.NotInRoom, host.Single<ChatOperationReply>().Status);
		Assert.Empty(host.All<MessagePacket>());
	}

	[Fact]
	public async Task Send_RepliesNotInRoom_AfterTheRoomWasClosed()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomWithMembersAsync("alice", "bob");

		await host.SendAsync("alice", "room.close", new CloseRoomRequest { RoomId = roomId });
		host.Clear();

		// 關房會清空成員名單，所以「房間關了」對送訊息的人來說就是「你不在任何房間」——
		// 少一個狀態、少一次查詢，這就是為什麼沒有 ROOM_CLOSED（ADR-8、ADR-10）。
		await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = "anyone?" });

		Assert.Equal(Status.NotInRoom, host.Single<ChatOperationReply>().Status);
		Assert.Empty(host.All<MessagePacket>());
	}

	[Fact]
	public async Task Send_AssignsStrictlyIncreasingOrderKeys()
	{
		await using var host = await CommandFlowHost.StartAsync();
		await host.CreateRoomWithMembersAsync("alice");

		foreach (var body in (string[])["one", "two", "three"])
			await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = body });

		var keys = host.All<MessagePacket>().Select(message => message.OrderKey).ToList();

		// 時鐘在 harness 裡是不動的，所以這三個號**完全來自單調守衛**——正好是那條路徑。
		Assert.Equal(keys.OrderBy(key => key), keys);
		Assert.Equal(3, keys.Distinct().Count());

		// order_key 低於 JS 的 2^53，webClient 可以直接當 number 用（ADR-2 不選 snowflake 的
		// 理由之一）。這條斷言釘住的是「別哪天把它換成 snowflake 卻忘了前端」。
		Assert.All(keys, key => Assert.True(key < 1L << 53));
	}

	[Fact]
	public async Task History_ReturnsWhatWasSent_NewestFirst()
	{
		await using var host = await CommandFlowHost.StartAsync();
		await host.CreateRoomWithMembersAsync("alice", "bob");

		foreach (var body in (string[])["one", "two", "three"])
			await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = body });

		host.Clear();

		// bob 沒送過訊息，但他在房間裡，所以查得到完整歷史。
		await host.SendAsync("bob", "chat.history", new ChatHistoryRequest());

		var history = host.Single<ChatHistory>();

		Assert.Equal(["three", "two", "one"], history.Messages.Select(message => message.Body));
		Assert.False(history.HasMore);
		Assert.Equal(["conn-bob"], host.TargetsOf<ChatHistory>());
	}

	[Fact]
	public async Task History_PagesBackwards_WithoutOverlapOrGaps()
	{
		await using var host = await CommandFlowHost.StartAsync();
		await host.CreateRoomWithMembersAsync("alice");

		foreach (var index in Enumerable.Range(1, 5))
			await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = $"m{index}" });

		host.Clear();

		await host.SendAsync("alice", "chat.history", new ChatHistoryRequest { Limit = 2 });
		var first = host.Single<ChatHistory>();
		host.Clear();

		Assert.Equal(["m5", "m4"], first.Messages.Select(message => message.Body));
		Assert.True(first.HasMore);

		// 往上滑：拿上一頁最小的 order_key 當游標。這正是 5.3「重連後補齊」用的那條路徑。
		await host.SendAsync(
			"alice",
			"chat.history",
			new ChatHistoryRequest { BeforeOrderKey = first.Messages[^1].OrderKey, Limit = 2 });

		Assert.Equal(["m3", "m2"], host.Single<ChatHistory>().Messages.Select(message => message.Body));
	}

	[Fact]
	public async Task History_IsScopedToTheRoomTheUserIsCurrentlyIn()
	{
		await using var host = await CommandFlowHost.StartAsync();

		await host.CreateRoomWithMembersAsync("alice");
		await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = "in the first room" });

		// 加入新房間會自動離開舊的。命令不帶 room_id，所以 alice **沒有辦法**回頭查前一間房
		// 的歷史——那個限制是結構性的（ADR-5）。
		var second = await host.CreateRoomAsync("alice", "Second", string.Empty);
		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = second });
		host.Clear();

		await host.SendAsync("alice", "chat.history", new ChatHistoryRequest());

		var history = host.Single<ChatHistory>();

		Assert.Equal(second, history.RoomId);
		Assert.Empty(history.Messages);
	}

	[Fact]
	public async Task History_CarriesTheDisplayNameSnapshot()
	{
		await using var host = await CommandFlowHost.StartAsync();
		await host.CreateRoomWithMembersAsync("alice");

		await host.Profiles.SaveAsync("alice", "Alice", null);
		await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = "before" });

		// 改名之後歷史訊息維持舊名字（ADR-3）：聊天記錄呈現「他當時叫什麼」。
		await host.Profiles.SaveAsync("alice", "Alicia", null);
		await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = "after" });

		host.Clear();
		await host.SendAsync("alice", "chat.history", new ChatHistoryRequest());

		Assert.Equal(
			[("after", "Alicia"), ("before", "Alice")],
			host.Single<ChatHistory>().Messages.Select(message => (message.Body, message.SenderDisplayName)));
	}

	[Fact]
	public async Task Reconnect_CatchesUpThroughHistory_NotThroughReplay()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomWithMembersAsync("alice", "bob");

		// bob 斷線。寬限期內成員資格還在，所以 alice 的訊息仍然會廣播給他（他收不到而已）。
		await host.Membership.MarkDisconnectedAsync("bob", host.ConnectionOf("bob"));
		await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = "while bob was away" });
		host.Clear();

		// bob 重連並重新加入，然後靠歷史補齊——這是 room-layer.md ADR-2 那句「寬限期內送出的
		// 訊息不補送，使用者重連後靠聊天層的歷史記錄補齊」的兌現（5.3）。
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		host.Clear();

		await host.SendAsync("bob", "chat.history", new ChatHistoryRequest());

		Assert.Equal(
			["while bob was away"],
			host.Single<ChatHistory>().Messages.Select(message => message.Body));
	}

	[Fact]
	public async Task RetentionSweeper_DeletesMessagesPastThePeriod_AndLeavesTheRestQueryable()
	{
		await using var host = await CommandFlowHost.StartAsync();
		await host.CreateRoomWithMembersAsync("alice");

		await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = "old" });

		// 把時鐘推過保留期，再送一則——所以第一則過期、第二則沒有。
		var retention = new ChatRetention(TimeSpan.FromDays(90), BatchSize: 5000, SweepInterval: TimeSpan.FromDays(1));
		host.Advance(TimeSpan.FromDays(100));

		await host.SendAsync("alice", "chat.send", new SendChatMessageRequest { Body = "new" });
		host.Clear();

		Assert.Equal(1, await host.SweepMessagesAsync(retention));

		await host.SendAsync("alice", "chat.history", new ChatHistoryRequest());

		Assert.Equal(["new"], host.Single<ChatHistory>().Messages.Select(message => message.Body));
	}
}
