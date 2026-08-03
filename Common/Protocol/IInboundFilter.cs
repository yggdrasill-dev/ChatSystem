namespace Common.Protocol;

public enum FilterDecision
{
	Allow,

	// 忽略這則封包，連線保留。
	Drop,

	// 視為狀態違反：這條連線整體不該存在。處置動作由 CommandRouter 統一執行。
	Terminate,
}

// 分派前的前置條件檢查，由各層自己註冊（見 protocol-layer.md ADR-6）。
// 刻意只給 connectionId 與 subject：filter 是前置條件檢查，不該需要理解 payload。
public interface IInboundFilter
{
	int Order { get; }

	ValueTask<FilterDecision> EvaluateAsync(
		string connectionId,
		string subject,
		CancellationToken cancellationToken = default);
}
