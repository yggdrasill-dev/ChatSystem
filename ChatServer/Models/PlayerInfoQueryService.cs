using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatServer.Models;

public class PlayerInfoQueryService(
	IMessageQueueService messageQueueService)
	: IGetService<PlayerInfoQuery, PlayerInfo?>
{
	public async ValueTask<PlayerInfo?> GetAsync(PlayerInfoQuery query)
	{
		var getPlayerQuery = new GetPlayerRequest
		{
			SessionId = query.SessionId
		};

		var queryReply = await messageQueueService
			.RequestAsync("session.get", getPlayerQuery.ToByteArray())
			.ConfigureAwait(false);

		var data = GetPlayerResponse.Parser.ParseFrom(queryReply.Data);

		return data.Player != null
			? new PlayerInfo(data.Player.SessionId, data.Player.ConnectorId, data.Player.Name)
			: null;
	}
}
