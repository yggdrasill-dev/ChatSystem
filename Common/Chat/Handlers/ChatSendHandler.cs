using Chat.Protos;
using Common.Identity;
using Common.Protocol;
using Common.Rooms;

namespace Common.Chat.Handlers;

internal sealed class ChatSendHandler(
	IRoomMembership membership,
	IUserProfileStore profiles,
	IMessageSequencer sequencer,
	IChatMessageStore store,
	IChatRateLimiter rateLimiter,
	RoomBroadcaster broadcaster,
	TimeProvider timeProvider) : IPacketHandler<SendChatMessageRequest>
{
	// 撞到同一個 order_key 需要同一間房、同一微秒、兩個 process 同時寫入（6.4）。房間的訊息
	// 速率是人類尺度的，實務上不會發生——所以連三次都撞到不是競爭，是壞了，該讓它叫出來。
	private const int MaxAppendAttempts = 3;

	public async ValueTask HandleAsync(
		CommandContext context,
		SendChatMessageRequest message,
		CancellationToken cancellationToken = default)
	{
		// 內容檢查放最前面：它不需要任何 I/O，擋掉的請求連 Redis 都不用碰。代價是這時還不知道
		// roomId，所以回覆的 room_id 是空字串——status 非零，不會序列化成 0 bytes。
		var rejection = ChatMessageDraft.Validate(message.Body);

		if (rejection != ChatMessageRejection.None)
		{
			await ReplyAsync(context, StatusOf(rejection), string.Empty, cancellationToken).ConfigureAwait(false);
			return;
		}

		// 這一步同時是**授權、定址、以及「房間還在嗎」**——關房＝刪房（ADR-10）且會清空成員
		// 名單，所以三件事一次查詢就夠（ADR-5）。命令刻意不帶 room_id，所以「你真的在這間房嗎」
		// 這個檢查不可能被漏掉。
		var roomId = await membership.GetCurrentRoomAsync(context.Principal, cancellationToken).ConfigureAwait(false);

		if (roomId is null)
		{
			await ReplyAsync(context, ChatOperationReply.Types.Status.NotInRoom, string.Empty, cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		if (!await rateLimiter.TryAcquireAsync(context.Principal, cancellationToken).ConfigureAwait(false))
		{
			await ReplyAsync(context, ChatOperationReply.Types.Status.RateLimited, roomId, cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		// 顯示名稱存的是**送出當下的快照**（ADR-3）：歷史查詢因此不需要 join、不需要批次查
		// profile、不需要 client 端的快取失效處理。改名不會改寫歷史訊息，這是刻意的語意。
		var displayName = await profiles
			.GetDisplayNameAsync(context.Principal, cancellationToken)
			.ConfigureAwait(false);

		var draft = new ChatMessageDraft(roomId, context.Principal, displayName ?? string.Empty, message.Body);
		var stored = await AppendAsync(draft, cancellationToken).ConfigureAwait(false);

		// **寫入成功之後才廣播，順序不能反過來**（ADR-1）。歷史記錄是真相來源——
		// room-layer.md ADR-2 承諾「寬限期內送出的訊息不補送，重連後靠聊天層的歷史記錄補齊」，
		// 先廣播後存會產生「在線的人看到了、歷史裡沒有」的訊息，那條承諾當場變成空的。
		var members = await membership.GetMembersAsync(roomId, cancellationToken).ConfigureAwait(false);

		// 送出者自己也在名單上，所以他會收到自己的訊息——那正是他拿到 order_key 的方式，
		// 因此成功路徑**不需要**額外回一則 OK。
		await broadcaster
			.ToUsersAsync([.. members.Select(member => member.UserId)], ChatPacket.Of(stored), cancellationToken)
			.ConfigureAwait(false);
	}

	private async ValueTask<ChatMessage> AppendAsync(ChatMessageDraft draft, CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			// 重新發號再試就好：寫入是單一插入、沒有交易，所以沒有回滾要處理（6.4）。
			var message = draft.WithOrder(sequencer.Next(), timeProvider.GetUtcNow());

			if (await store.TryAppendAsync(message, cancellationToken).ConfigureAwait(false))
				return message;

			if (attempt >= MaxAppendAttempts)
				// 刻意往上丟：協定層會記 error 並回 HANDLER_FAILED，client 什麼都收不到。
				// 這是**內部故障**不是業務失敗，所以不走 ChatOperationReply——那個型別留給
				// 「使用者做錯了什麼」，這是 protocol-layer.md ADR-8 劃的界線。
				throw new InvalidOperationException(
					$"連續 {MaxAppendAttempts} 次都撞到同一個 order_key（room={draft.RoomId}）。");
		}
	}

	private ValueTask ReplyAsync(
		CommandContext context,
		ChatOperationReply.Types.Status status,
		string roomId,
		CancellationToken cancellationToken) =>
		broadcaster.ReplyAsync(context, ChatPacket.Reply(status, roomId), cancellationToken);

	private static ChatOperationReply.Types.Status StatusOf(ChatMessageRejection rejection) => rejection switch
	{
		ChatMessageRejection.BodyEmpty => ChatOperationReply.Types.Status.BodyEmpty,
		ChatMessageRejection.BodyTooLong => ChatOperationReply.Types.Status.BodyTooLong,
		_ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, "沒有對應的下行狀態。")
	};
}
