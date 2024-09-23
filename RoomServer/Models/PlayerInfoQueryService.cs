using System.Collections.Generic;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace RoomServer.Models;

public class PlayerInfoQueryService(
	IMessageQueueService messageQueueService,
	ICommandService<LeaveRoomCommand> leaveRoomService) : IQueryService<PlayerInfoQuery, PlayerInfo>
{
	public async IAsyncEnumerable<PlayerInfo> QueryAsync(PlayerInfoQuery query)
	{
		foreach (var sessionId in query.SessionIds)
		{
			var getPlayerQuery = new GetPlayerRequest
			{
				SessionId = sessionId
			};

			var queryReply = await messageQueueService
				.RequestAsync("session.get", getPlayerQuery.ToByteArray())
				.ConfigureAwait(false);

			var data = GetPlayerResponse.Parser.ParseFrom(queryReply.Data);

			if (data.Player == null)
				await leaveRoomService
					.ExecuteAsync(new LeaveRoomCommand
					{
						SessionId = sessionId,
						Room = "test"
					})
					.ConfigureAwait(false);
			else
				yield return new PlayerInfo(data.Player.SessionId, data.Player.ConnectorId, data.Player.Name);
		}
	}
}
