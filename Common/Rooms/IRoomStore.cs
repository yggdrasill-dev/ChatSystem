namespace Common.Rooms;

// 房間本身的儲存。是持久資料，目前的 Redis 實作是 provisional——正式儲存的選擇跟聊天層的
// 訊息記錄一起做，見 room-layer.md ADR-7。
//
// 每個變更操作都是「一次到位、回傳有沒有生效」，刻意不提供 GetAsync + UpdateAsync 的
// read-modify-write：那會 lost update，而併發語意不是事後可以換掉的東西。
//
// **房間只有「在」與「不在」**（chat-layer.md ADR-10）：沒有已關閉的房間這種狀態，所以每個
// 方法的失敗情況都只有一種——房間不存在。
public interface IRoomStore
{
	ValueTask<Room?> GetAsync(string roomId, CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyCollection<Room>> ListAsync(CancellationToken cancellationToken = default);

	// 已存在則回 false，不覆寫。這是唯一需要真正原子的操作。
	ValueTask<bool> TryCreateAsync(Room room, CancellationToken cancellationToken = default);

	// 房間不存在則回 false。
	ValueTask<bool> TryUpdateSettingsAsync(
		string roomId,
		string name,
		string? passwordHash,
		CancellationToken cancellationToken = default);

	// 真的刪除：房間紀錄與它的封鎖名單一起消失，歷史訊息也是（ADR-10；Postgres 版靠
	// ON DELETE CASCADE，Redis 版自己刪）。房間不存在則回 false——重複刪除因此天生 idempotent，
	// 而且「只有一個呼叫端刪得到」讓 RoomClosed 不會被廣播兩次。
	ValueTask<bool> TryDeleteAsync(string roomId, CancellationToken cancellationToken = default);
}
