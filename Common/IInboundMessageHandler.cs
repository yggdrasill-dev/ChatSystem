using Google.Protobuf;

namespace Common;

// Gateway 收到一則 client 封包時呼叫的擴充點。連線層本身不理解 subject 的業務語意，
// 只負責把「哪條連線、什麼 subject、什麼內容」交出去；由誰接手處理是使用者管理層/業務層的事，
// 目前尚未設計，故先提供一個最小的預設實作。
public interface IInboundMessageHandler
{
	ValueTask HandleAsync(string connectionId, string subject, ByteString payload, CancellationToken cancellationToken = default);
}
