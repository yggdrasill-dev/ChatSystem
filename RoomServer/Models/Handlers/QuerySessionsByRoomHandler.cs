using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using NATS.Client;

namespace RoomServer.Models.Handlers;

public class QuerySessionsByRoomHandler(
	IMessageQueueService messageQueueService,
	IQueryService<RoomSessionsQuery, string> roomSessionsService,
	IQueryService<PlayerInfoQuery, PlayerInfo> playerInfoService) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		var request = RoomSessionsRequest.Parser.ParseFrom(msg.Data);

		var sessionIds = roomSessionsService
			.QueryAsync(new RoomSessionsQuery
			{
				Room = request.Room
			})
			.ToEnumerable()
			.ToArray();

		var allPlayers = playerInfoService.QueryAsync(new PlayerInfoQuery
		{
			SessionIds = sessionIds
		});

		var response = new RoomSessionsResponse();

		response.Players.AddRange(allPlayers
			.Select(player => new Chat.Protos.PlayerInfo
			{
				SessionId = player.SessionId,
				ConnectorId = player.ConnectorId,
				Name = player.Name
			})
			.ToEnumerable());

		await messageQueueService.PublishAsync(msg.Reply, response.ToByteArray());
	}
}
