namespace Common.Rooms;

// 房間的成員名單：roomId → {成員}，外加 userId → roomId 的反向指向（一次只能在一間房，
// 所以反向是單一值）。這是**暫時狀態**，不是持久資料——房間關掉之後不需要知道誰曾經在裡面。
public interface IRoomMembership
{
	// 寬限期已過的成員在讀取時就被濾掉，呼叫端不需要自己算。正確性因此不依賴 sweeper
	// （room-layer.md ADR-2）——sweeper 掛掉只會讓「離開」的廣播遲到。
	ValueTask<IReadOnlyCollection<RoomMember>> GetMembersAsync(string roomId, CancellationToken cancellationToken = default);

	ValueTask<string?> GetCurrentRoomAsync(string userId, CancellationToken cancellationToken = default);

	// 同時做三件事：寫入成員、把 userId 指向這個房間、清掉可能存在的寬限期標記。
	ValueTask JoinAsync(string roomId, string userId, string connectionId, CancellationToken cancellationToken = default);

	// 移除成員。userId → roomId 的指向只有在還指向這個房間時才會被清掉（fencing，見實作）。
	ValueTask RemoveAsync(string roomId, string userId, CancellationToken cancellationToken = default);

    // 只有當該成員目前的 CurrentConnectionId 等於傳入值時才標記（ADR-4 的 fencing）。
	//
	// 參數是 userId 而不是只有 connectionId：成員以 userId 為鍵存放，只給 connectionId 就
	// 必須維護一張 connectionId → (roomId, userId) 的反向索引。斷線事件會帶 principal
	// （`connection-layer.md` ADR-9），所以那張表不需要存在。
	ValueTask MarkDisconnectedAsync(string userId, string connectionId, CancellationToken cancellationToken = default);

	// 給 sweeper 用：寬限期已過、還沒被移除的成員。
	ValueTask<IReadOnlyCollection<(string RoomId, string UserId)>> ListExpiredAsync(CancellationToken cancellationToken = default);
}
