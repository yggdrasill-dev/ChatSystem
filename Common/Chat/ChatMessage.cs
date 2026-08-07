namespace Common.Chat;

public enum ChatMessageRejection
{
	None,
	BodyEmpty,
	BodyTooLong,
}

// 尚未定序的訊息。訊息的有效性是領域的性質，不是 schema 的 NOT NULL、也不是 handler 裡
// 隨手寫的兩個 if——把它放在這裡，規則只有一個地方可以改。
public sealed record ChatMessageDraft(
	string RoomId,
	string SenderUserId,
	string SenderDisplayName,
	string Body)
{
	// 暫定值，未經任何驗證（chat-layer.md §9）。這個上限防的是「一則聊天訊息該多長」，
	// 跟連線層那個 256 KB 不是同一件事——後者防的是記憶體與 NATS max_payload（ADR-7）。
	public const int MaxBodyLength = 4096;

	public static ChatMessageRejection Validate(string body) => body switch
	{
		_ when string.IsNullOrWhiteSpace(body) => ChatMessageRejection.BodyEmpty,
		_ when body.Length > MaxBodyLength => ChatMessageRejection.BodyTooLong,
		_ => ChatMessageRejection.None
	};

	public ChatMessage WithOrder(long orderKey, DateTimeOffset sentAt) =>
		new(RoomId, orderKey, SenderUserId, SenderDisplayName, Body, sentAt);
}

// 已定序的訊息。從 draft 到這裡的那一步就是「發號」，而發號的擁有者是可以換的（現在是
// 程序內的時鐘，未來可能是房間 actor，見 ADR-9）——用兩個型別把那一步標示出來，換擁有者
// 時就知道要動哪裡。
public sealed record ChatMessage(
	string RoomId,
	long OrderKey,
	string SenderUserId,
	string SenderDisplayName,
	string Body,
	DateTimeOffset SentAt);

public sealed record ChatMessagePage(
	// order_key 由大到小。
	IReadOnlyList<ChatMessage> Messages,
	bool HasMore);
