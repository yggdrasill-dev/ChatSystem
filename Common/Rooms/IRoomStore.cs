namespace Common.Rooms;

// 房間本身的儲存。是持久資料（房間關掉之後歷史訊息還要能查），目前的 Redis 實作是
// provisional——正式儲存的選擇跟聊天層的訊息記錄一起做，見 room-layer.md ADR-7。
//
// 每個變更操作都是「一次到位、回傳有沒有生效」，刻意不提供 GetAsync + UpdateAsync 的
// read-modify-write：那會 lost update，而併發語意不是事後可以換掉的東西。
public interface IRoomStore
{
	ValueTask<Room?> GetAsync(string roomId, CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyCollection<Room>> ListOpenAsync(CancellationToken cancellationToken = default);

	// 已存在則回 false，不覆寫。這是唯一需要真正原子的操作。
	ValueTask<bool> TryCreateAsync(Room room, CancellationToken cancellationToken = default);

	// 房間不存在或已關閉則回 false。
	ValueTask<bool> TryUpdateSettingsAsync(
		string roomId,
		string name,
		string? passwordHash,
		CancellationToken cancellationToken = default);

	// 房間不存在或已經是關閉狀態則回 false。
	ValueTask<bool> TryCloseAsync(string roomId, CancellationToken cancellationToken = default);
}
