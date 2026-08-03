namespace Common.Rooms;

// 房間的封鎖名單，加入房間時檢查。跟 IRoomStore 同一個儲存（見 room-layer.md ADR-7）。
//
// 這裡不需要 Try 語意：底層的集合操作本身就是原子的，而且封鎖/解鎖天生 idempotent
// ——重複封鎖同一個人跟封鎖一次的結果一樣。
public interface IRoomBanList
{
	ValueTask<bool> IsBannedAsync(string roomId, string userId, CancellationToken cancellationToken = default);

	ValueTask BanAsync(string roomId, string userId, CancellationToken cancellationToken = default);

	ValueTask UnbanAsync(string roomId, string userId, CancellationToken cancellationToken = default);
}
