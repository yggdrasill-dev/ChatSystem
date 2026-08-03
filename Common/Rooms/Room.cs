namespace Common.Rooms;

public sealed record Room(
	string RoomId,
	string Name,
	// null = 公開房。密碼存雜湊、不存可還原的形式，見 room-layer.md ADR-6。
	string? PasswordHash,
	// 刻意不可變更。後台流程（kick/close/update）都是「先讀房間檢查是不是房主、再動作」，
	// 看起來像 TOCTOU 但因為房主永遠不會變所以安全——如果以後要加「轉移房主」，那三個流程都要重新檢視。
	string OwnerUserId,
	DateTimeOffset CreatedAt,
	bool IsClosed);
