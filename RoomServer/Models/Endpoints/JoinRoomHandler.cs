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
	public class JoinRoomHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly ICommandService<JoinRoomCommand> m_JoinRoomService;
		private readonly IQueryService<RoomSessionsQuery, string> m_ListRoomSessionsService;
		private readonly ILogger<JoinRoomHandler> m_Logger;

		public JoinRoomHandler(
			IMessageQueueService messageQueueService,
			ICommandService<JoinRoomCommand> joinRoomService,
			IQueryService<RoomSessionsQuery, string> listRoomSessionsService,
			ILogger<JoinRoomHandler> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_JoinRoomService = joinRoomService ?? throw new ArgumentNullException(nameof(joinRoomService));
			m_ListRoomSessionsService = listRoomSessionsService ?? throw new ArgumentNullException(nameof(listRoomSessionsService));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var packet = QueuePacket.Parser.ParseFrom(msg.Data);

			var joinRoomData = JoinRoom.Parser.ParseFrom(packet.Payload);

			var roomSessionIds = m_ListRoomSessionsService
				.QueryAsync(new RoomSessionsQuery
				{
					Room = joinRoomData.Room
				})
				.ToEnumerable()
				.ToArray();

			await m_JoinRoomService.ExecuteAsync(new JoinRoomCommand
			{
				SessionId = packet.SessionId,
				Room = joinRoomData.Room
			}).ConfigureAwait(false);

			var response = new SendPacket
			{
				Subject = "room.join.reply"
			};

			response.SessionIds.Add(packet.SessionId);

			await m_MessageQueueService.PublishAsync(
				"connect.send",
				response.ToByteArray()).ConfigureAwait(false);

			if (roomSessionIds.Length > 0)
			{
				var broadcast = new SendPacket
				{
					Subject = "chat.receive"
				};

				broadcast.SessionIds.AddRange(roomSessionIds);

				var chatMessage = new ChatMessage
				{
					Scope = Scope.Room,
					From = "System",
					Message = $"New player joined!"
				};

				broadcast.Payload = chatMessage.ToByteString();

				await m_MessageQueueService.PublishAsync(
					"connect.send",
					broadcast.ToByteArray()).ConfigureAwait(false);

				var refrashPacket = new SendPacket
				{
					Subject = "room.player.refrash"
				};

				refrashPacket.SessionIds.AddRange(roomSessionIds);

				await m_MessageQueueService.PublishAsync(
					"connect.send",
					refrashPacket.ToByteArray()).ConfigureAwait(false);
			}

			m_Logger.LogInformation($"({packet.SessionId}, {joinRoomData.Room}) joined.");
		}
	}
}
