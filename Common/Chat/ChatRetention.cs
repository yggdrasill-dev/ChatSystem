namespace Common.Chat;

// 保留期限的政策值。**全部是暫定值、未經任何負載測試**（chat-layer.md §9），處理方式比照
// room-layer.md 的 30 秒寬限期：集中在一個型別上、明確標記，而不是散成各處的魔術數字。
public sealed record ChatRetention(TimeSpan Period, int BatchSize, TimeSpan SweepInterval)
{
	public static readonly ChatRetention Default = new(
		Period: TimeSpan.FromDays(90),
		BatchSize: 5000,
		SweepInterval: TimeSpan.FromDays(1));
}
