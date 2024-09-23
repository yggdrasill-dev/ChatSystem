using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace RoomServer.Models.Handlers;

public class LeaveRoomHandler(
	IMessageQueueService messageQueueService,
	ICommandService<LeaveRoomCommand> leaveRoomService,
	IQueryService<RoomSessionsQuery, string> listRoomSessionsService,
	ILogger<LeaveRoomHandler> logger) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		var request = LeaveRoomRequest.Parser.ParseFrom(msg.Data);

		await leaveRoomService.ExecuteAsync(new LeaveRoomCommand
		{
			SessionId = request.SessionId,
			Room = request.Room
		}).ConfigureAwait(false);

		var roomSessionIds = listRoomSessionsService
			.QueryAsync(new RoomSessionsQuery
			{
				Room = request.Room
			})
			.ToEnumerable();

		var refrashPacket = new SendPacket
		{
			Subject = "room.player.refrash"
		};

		refrashPacket.SessionIds.AddRange(roomSessionIds);

		await messageQueueService.PublishAsync(
			"connect.send",
			refrashPacket.ToByteArray()).ConfigureAwait(false);

		await messageQueueService.PublishAsync(msg.Reply, []).ConfigureAwait(false);

		logger.LogInformation("({SessionId}, {Room}) leaved.", request.SessionId, request.Room);
	}
}
