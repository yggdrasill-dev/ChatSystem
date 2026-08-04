using Google.Protobuf;

namespace Common;

// Gateway 收到一則 client 封包時呼叫的擴充點。連線層本身不理解 subject 的業務語意，
// 只負責把「哪條連線、誰、什麼 subject、什麼內容」交出去；接手的是協定層的 InboundBridge。
//
// principal 是 handshake 驗證通過的不透明字串（連線層轉手、不解讀）。它在這裡出現的唯一
// 理由是省掉上層「每則命令查一次 connectionId -> 身分」的往返。
public interface IInboundMessageHandler
{
	ValueTask HandleAsync(
		string connectionId,
		string principal,
		string subject,
		ByteString payload,
		CancellationToken cancellationToken = default);
}
