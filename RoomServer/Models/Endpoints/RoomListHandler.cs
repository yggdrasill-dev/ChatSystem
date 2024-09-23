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

public class RoomListHandler(
	IMessageQueueService messageQueueService,
	IQueryService<RoomListQuery, RoomInfo> listRoomService,
	ILogger<RoomListHandler> logger) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		logger.LogInformation($"RoomList received.");

		var packet = QueuePacket.Parser.ParseFrom(msg.Data);

		var rooms = listRoomService.QueryAsync(new RoomListQuery());

		var response = new RoomList();

		response.Rooms.AddRange(rooms
			.Select(info => new Room
			{
				Name = info.Name,
				HasPassword = info.HasPassword
			}).ToEnumerable());

		var sendMsg = new SendPacket
		{
			Subject = "chat.room.list"
		};

		sendMsg.SessionIds.Add(packet.SessionId);
		sendMsg.Payload = response.ToByteString();

		await messageQueueService
			.PublishAsync("connect.send", sendMsg.ToByteArray())
			.ConfigureAwait(false);
	}
}
