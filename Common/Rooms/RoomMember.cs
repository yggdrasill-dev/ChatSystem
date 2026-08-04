namespace Common.Rooms;

// 成員以 UserId 為鍵，不是 ConnectionId——這是「斷線重連對其他成員無感」逼出來的：
// 寬限期內連線已經不存在，只有 UserId 能代表這個成員（room-layer.md ADR-1）。
public sealed record RoomMember(
	string UserId,
	DateTimeOffset JoinedAt,
	// 只用於斷線標記的 fencing，不用於投遞——投遞一律走 userId → connectionId 的即時解析。
	string CurrentConnectionId,
	// 非 null = 正在寬限期中。
	DateTimeOffset? DisconnectedAt);
