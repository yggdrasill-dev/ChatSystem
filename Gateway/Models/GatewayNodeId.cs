namespace Gateway.Models;

// 這個 Gateway process 自己的節點識別碼，程式啟動時產生一次。
// 用來組 ConnectionDirectory 的值，以及這個節點專屬的 connect.deliver.{nodeId} subject。
public sealed record GatewayNodeId(string Value)
{
	public override string ToString() => Value;
}
