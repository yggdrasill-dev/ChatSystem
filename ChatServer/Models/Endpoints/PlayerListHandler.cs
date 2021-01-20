using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace ChatServer.Models.Endpoints
{
	public class PlayerListHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IQueryService<PlayerInfoQuery, PlayerInfo> m_PlayerQueryService;
		private readonly IQueryService<ListPlayerQuery, string> m_RoomListService;
		private readonly ILogger<PlayerListHandler> m_Logger;

		public PlayerListHandler(
			IMessageQueueService messageQueueService,
			IQueryService<PlayerInfoQuery, PlayerInfo> playerQueryService,
			IQueryService<ListPlayerQuery, string> roomListService,
			ILogger<PlayerListHandler> logger)
		{
			m_MessageQueueService = messageQueueService;
			m_PlayerQueryService = playerQueryService ?? throw new ArgumentNullException(nameof(playerQueryService));
			m_RoomListService = roomListService ?? throw new ArgumentNullException(nameof(roomListService));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var packet = QueuePacket.Parser.ParseFrom(msg.Data);

			m_Logger.LogInformation($"chat.player.list => Receive packet from {packet.SessionId}");

			var responseSessionIds = m_RoomListService.QueryAsync(new ListPlayerQuery
			{
				Room = "test"
			});

			var playersContent = new PlayerList();

			var allPlayers = m_PlayerQueryService.QueryAsync(new PlayerInfoQuery
			{
				SessionIds = await responseSessionIds.ToArrayAsync().ConfigureAwait(false)
			});

			playersContent.Players.AddRange(allPlayers.Select(player => player.Name).ToEnumerable());

			var sendMsg = new SendPacket
			{
				Subject = "chat.player.list",
				Payload = playersContent.ToByteString()
			};

			sendMsg.SessionIds.Add(packet.SessionId);

			await m_MessageQueueService.PublishAsync("connect.send", sendMsg.ToByteArray());
		}
	}
}
