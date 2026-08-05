using Adaptare;
using Chat.Protos;
using Google.Protobuf;

namespace Common.Connections;

// 跟系統裡其他每一條通道一樣走 Adaptare 的 IMessageSender。
//
// 這裡曾經改用原生 INatsConnection，理由寫的是「Adaptare 的 publish 不會落在字面 subject」
// ——**那個診斷是錯的**。用一個原生 ">" 全捕捉訂閱實測過：Adaptare 發出來的訊息 subject 是
// 字面值、payload 是原封不動的 byte[]、落在正確那台 server，跟原生發的唯一差別是多帶一個
// 空的 headers 集合。
//
// 真正的原因在呼叫端：斷線清理把「已經取消的 CancellationToken」傳了進來（見
// GatewayWebSocketEndpoint 的 finally）。Adaptare 尊重取消，所以丟 OperationCanceledException、
// 訊息沒上 wire；原生 client 對已取消的 token 不理會，所以照樣送出去。換成原生只是把
// 取消 bug 蓋掉，並沒有修掉它。
internal sealed class ConnectionEventPublisher(IMessageSender messageSender) : IConnectionEventPublisher
{
	// 刻意不放在 connect.* 家族裡：那個前綴目前的意思是「投遞給某個 Gateway 節點」
	// （connect.deliver.{nodeId}、connect.terminate.{nodeId}），而這是反方向的事件廣播，
	// 沒有特定目標角色。用 events.* 開一個明確的事件命名空間，也避免 connect./connection.
	// 只差三個字母的辨識風險。
	private const string DisconnectedSubject = "events.connection.disconnected";

	public ValueTask PublishDisconnectedAsync(
		string connectionId,
		string nodeId,
		string principal,
		CancellationToken cancellationToken = default)
	{
		var message = new ConnectionDisconnected
		{
			ConnectionId = connectionId,
			NodeId = nodeId,
			Principal = principal
		};

		return messageSender.PublishAsync(DisconnectedSubject, message.ToByteArray(), cancellationToken);
	}
}
