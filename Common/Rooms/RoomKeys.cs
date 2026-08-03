using StackExchange.Redis;

namespace Common.Rooms;

// 房間層的 Redis key 設計。
//
// {rooms} 是 Cluster 的 hash tag，讓房間層所有 key 落在同一個 slot。刻意的取捨：代價是
// 這批資料集中在一個節點、無法靠 Cluster 分散，換來的是同一個操作可以跨 key 保持一致
// （例如未來真的需要 Lua 時）。房間資料量小、變更頻率低，可以接受。
//
// 連線層走的是相反選擇——Conn:{connectionId} 刻意分散在不同 slot，也因此
// ResolveNodesAsync 不能用 MGET。見 connection-layer.md 6.2。
internal static class RoomKeys
{
	public static readonly RedisKey Index = "{rooms}:index";

	public static RedisKey Room(string roomId) => $"{{rooms}}:room:{roomId}";

	public static RedisKey Ban(string roomId) => $"{{rooms}}:ban:{roomId}";

	public static class Fields
	{
		public const string Name = "name";
		public const string PasswordHash = "password_hash";
		public const string OwnerUserId = "owner_user_id";
		public const string CreatedAt = "created_at";
		public const string IsClosed = "is_closed";
	}
}
