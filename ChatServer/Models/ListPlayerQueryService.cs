using System.Collections.Generic;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatServer.Models;

public class ListPlayerQueryService(IMessageQueueService messageQueueService) : IQueryService<ListPlayerQuery, PlayerInfo>
{
	public async IAsyncEnumerable<PlayerInfo> QueryAsync(ListPlayerQuery query)
	{
		var roomQuery = new RoomSessionsRequest
		{
			Room = query.Room
		};

		var result = await messageQueueService.RequestAsync("room.query", roomQuery.ToByteArray());
		var queryResponse = RoomSessionsResponse.Parser.ParseFrom(result.Data);

		foreach (var player in queryResponse.Players)
			yield return new PlayerInfo(player.SessionId, player.ConnectorId, player.Name);
	}
}
