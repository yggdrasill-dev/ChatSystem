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
// 刻意只給 context 與 subject：filter 是前置條件檢查，不該需要理解 payload。
//
// 目前沒有任何實作。這個機制原本的第一個使用者是身分層的「未綁定身分前只接受
// identity.bind」，而 handshake 驗證讓那個狀態不存在了；保留機制是為了未來的 per-user
// 業務限流與跨命令前置條件。
public interface IInboundFilter
{
	int Order { get; }

	ValueTask<FilterDecision> EvaluateAsync(
		CommandContext context,
		string subject,
		CancellationToken cancellationToken = default);
}
