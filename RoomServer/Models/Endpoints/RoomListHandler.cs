using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace RoomServer.Models.Endpoints
{
	public class RoomListHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IQueryService<RoomListQuery, RoomInfo> m_ListRoomService;
		private readonly ILogger<RoomListHandler> m_Logger;

		public RoomListHandler(
			IMessageQueueService messageQueueService,
			IQueryService<RoomListQuery, RoomInfo> listRoomService,
			ILogger<RoomListHandler> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_ListRoomService = listRoomService ?? throw new ArgumentNullException(nameof(listRoomService));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			m_Logger.LogInformation($"RoomList received.");

			var packet = QueuePacket.Parser.ParseFrom(msg.Data);

			var rooms = m_ListRoomService.QueryAsync(new RoomListQuery());

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

			await m_MessageQueueService
				.PublishAsync("connect.send", sendMsg.ToByteArray())
				.ConfigureAwait(false);
		}
	}
}
