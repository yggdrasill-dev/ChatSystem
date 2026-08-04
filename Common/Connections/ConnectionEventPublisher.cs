using Chat.Protos;
using Google.Protobuf;
using NATS.Client.Core;

namespace Common.Connections;

// 直接用原生 NATS client 發，不經過 Adaptare 的 IMessageSender。
//
// 理由是對稱：訂閱端（房間層的 RoomDisconnectSubscriber）也是原生訂的。這條事件通道是整個
// 系統唯一「連線層 → 業務層」的方向，而它原本是唯一「一端 Adaptare 發、另一端原生訂」的
// 組合——結果事件根本到不了訂閱端（同一個 IMessageSender 發 dispatch.deliver 會到、發這個
// subject 不會到，差別只在 subject 字串，所以是 Adaptare 的 subject 對應規則）。
//
// dispatch.* / connect.* 目前仍走 Adaptare，因為那些路徑的兩端都是 Adaptare，對得上。
internal sealed class ConnectionEventPublisher(INatsConnection connection) : IConnectionEventPublisher
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

		return connection.PublishAsync(
			DisconnectedSubject,
			message.ToByteArray(),
			cancellationToken: cancellationToken);
	}
}
