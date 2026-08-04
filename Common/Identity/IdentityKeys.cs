using StackExchange.Redis;

namespace Common.Identity;

// 身分層的 Redis key 設計。
//
// 刻意不加 Cluster hash tag——不同使用者的 key 分散在不同 slot。代價是
// ResolveConnectionsAsync 不能用單一 MGET（要平行送出多個 GET，比照
// RedisConnectionDirectory），換來的是這批資料真的能靠 Cluster 分散。
//
// 房間層走的是相反選擇（{rooms} 集中在同一 slot），理由是房間資料量小、變更頻率低，
// 而且需要「同一個操作跨 key 保持一致」。見 identity-layer.md 6.2 與 room-layer.md 6.1。
internal static class IdentityKeys
{
	public static RedisKey Session(string sessionToken) => $"Session:{sessionToken}";

	public static RedisKey Presence(string userId) => $"Presence:{userId}";

	public static RedisKey LoginNonce(string nonce) => $"LoginNonce:{nonce}";

	public static RedisKey Profile(string userId) => $"Profile:{userId}";

	public static class ProfileFields
	{
		public const string DisplayName = "display_name";
		public const string PictureUrl = "picture_url";
	}
}
