using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace RoomServer.Models.Endpoints;

public class PlayerListHandler(
	IMessageQueueService messageQueueService,
	IGetService<GetRoomBySessionIdQuery, string?> getRoomService,
	IQueryService<PlayerInfoQuery, PlayerInfo> playerQueryService,
	IQueryService<RoomSessionsQuery, string> roomListService,
	ILogger<PlayerListHandler> logger) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		var packet = QueuePacket.Parser.ParseFrom(msg.Data);

		logger.LogInformation("chat.player.list => Receive packet from {SessionId}", packet.SessionId);

		var playerInRoom = await getRoomService
			.GetAsync(new GetRoomBySessionIdQuery
			{
				SessionId = packet.SessionId
			}).ConfigureAwait(false);

		if (string.IsNullOrEmpty(playerInRoom))
			return;

		var fromPlayerInfo = await playerQueryService
			.QueryAsync(new PlayerInfoQuery
			{
				SessionIds = [packet.SessionId]
			})
			.FirstOrDefaultAsync(cancellationToken: cancellationToken)
			.ConfigureAwait(false);

		if (fromPlayerInfo?.SessionId != packet.SessionId)
			return;

		var responseSessionIds = roomListService.QueryAsync(new RoomSessionsQuery
		{
			Room = playerInRoom
		});

		var playersContent = new PlayerList
		{
			Room = playerInRoom
		};

		var allPlayers = playerQueryService.QueryAsync(new PlayerInfoQuery
		{
			SessionIds = await responseSessionIds.ToArrayAsync(cancellationToken: cancellationToken).ConfigureAwait(false)
		});

		playersContent.Players.AddRange(allPlayers.Select(player => player.Name).ToEnumerable());

		var sendMsg = new SendPacket
		{
			Subject = "chat.player.list",
			Payload = playersContent.ToByteString()
		};

		sendMsg.SessionIds.Add(packet.SessionId);

		await messageQueueService.PublishAsync($"connect.send.{fromPlayerInfo.ConnectorId}", sendMsg.ToByteArray());
	}
}
