using Chat.Protos;
using Google.Protobuf;
using NATS.Client.Core;

namespace Common.Connections;

// 直接用原生 NATS client 發，不經過 Adaptare 的 IMessageSender。
//
// 理由是**對稱**，不是「這條通道必須用原生」：訂閱端（房間層的 RoomDisconnectSubscriber）
// 是原生訂的，而實測 Adaptare 的 publish 不是「把 payload 原樣發到字面 subject」——所以
// 「Adaptare 發、原生訂」這個組合收不到任何東西。兩端一致就會通，用哪一套都行。
//
// Adaptare 到底在 wire 上送什麼**目前不知道**（sniffer 沒連上，wire 沒被看到）。
// dispatch.* / connect.* 仍走 Adaptare，因為那些路徑兩端都是 Adaptare，不管它怎麼改寫都
// 對得上。完整的排除過程與留下的未解問題見 room-layer.md 第 9 節。
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
