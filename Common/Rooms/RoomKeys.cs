using StackExchange.Redis;

namespace Common.Rooms;

// 房間層留在 Redis 的那一半的 key 設計：**只有成員名單**。房間與封鎖名單在 B2 搬去 Postgres，
// `{rooms}:index` / `{rooms}:room:*` / `{rooms}:ban:*` 與那組 hash 欄位常數一起消失了。
//
// {rooms} 是 Cluster 的hash tag，讓這幾個 key 落在同一個 slot。刻意的取捨：代價是這批資料集中在
// 一個節點、無法靠 Cluster 分散，換來的是同一個操作可以跨 key 保持一致——`RedisRoomMembership`
// 的兩段 Lua（標記斷線、釋放 userId → roomId 指向）就是靠它。
//
// **先前這裡與 AddRoomStore 都寫成「成員名單要跟房間資料跨 key 一起操作」，那句話是錯的**：那兩段
// Lua 動的是 Members / Grace / UserRoom，一個都不碰房間資料。hash tag 仍然需要，但要的是下面這
// 三個 key 之間的一致，跟房間住在哪裡無關——所以 B2 把房間搬走並沒有動到這個取捨。
//
// 連線層走的是相反選擇——`Conn:{connectionId}` 刻意分散在不同 slot，也因此 ResolveNodesAsync
// 不能用 MGET。見 connection-layer.md 6.2。
internal static class RoomKeys
{
	// 成員名單：field = userId、value = packed（見 RedisRoomMembership）。
	public static RedisKey Members(string roomId) => $"{{rooms}}:members:{roomId}";

	// 一次只能在一間房，所以是單一值而不是集合。
	public static RedisKey UserRoom(string userId) => $"{{rooms}}:userroom:{userId}";

	// sweeper 的待辦清單：score = 寬限期到期時間、member = "roomId|userId"。
	// 有這個 sorted set，「掃出過期成員」就是一次 ZRANGEBYSCORE，不必掃過所有房間。
	public static readonly RedisKey Grace = "{rooms}:grace";
}
