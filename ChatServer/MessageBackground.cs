using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using ChatServer.Models;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatServer
{
	public class MessageBackground : BackgroundService
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IServiceProvider m_ServiceProvider;
		private readonly ILogger<MessageBackground> m_Logger;

		public MessageBackground(IMessageQueueService messageQueueService, IServiceProvider serviceProvider, ILogger<MessageBackground> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new System.ArgumentNullException(nameof(messageQueueService));
			m_ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
			m_Logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			var subscriptions = new List<IDisposable>();

			subscriptions.Add(await m_MessageQueueService.SubscribeAsync("chat.send", "chat", async (sender, args) =>
			{
				using var scope = m_ServiceProvider.CreateScope();

				var packet = QueuePacket.Parser.ParseFrom(args.Message.Data);

				m_Logger.LogInformation($"Receive packet from {packet.SessionId}");

				var msg = ChatContent.Parser.ParseFrom(packet.Payload);
				m_Logger.LogInformation($"Scope: {msg.Scope}, Target: {msg.Target}, Message: {msg.Message}");

				var getPlayerService = scope.ServiceProvider.GetRequiredService<IQueryService<GetPlayerQuery, PlayerInfo>>();
				var roomListService = scope.ServiceProvider.GetRequiredService<IQueryService<RoomListQuery, string>>();

				var playerInfo = await getPlayerService.QueryAsync(new GetPlayerQuery
				{
					SessionIds = new[] { packet.SessionId }
				}).FirstOrDefaultAsync().ConfigureAwait(false);

				if (playerInfo?.SessionId != packet.SessionId)
					return;

				var sendContent = new ChatContent
				{
					Scope = msg.Scope,
					From = playerInfo.Name,
					Message = msg.Message
				};

				var sendMsg = new SendPacket
				{
					Subject = "chat.receive"
				};

				var responseSessionIds = await roomListService.QueryAsync(new RoomListQuery
				{
					Room = "test"
				}).ToArrayAsync().ConfigureAwait(false);

				switch (msg.Scope)
				{
					case Scope.Room:

						sendMsg.SessionIds.AddRange(responseSessionIds);
						break;

					case Scope.Person:

						var targetName = msg.Target;

						var roomPlayers = getPlayerService.QueryAsync(new GetPlayerQuery
						{
							SessionIds = responseSessionIds
						});

						var matchedPlayer = await roomPlayers
							.Where(player => player.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
							.FirstOrDefaultAsync()
							.ConfigureAwait(false);

						if (matchedPlayer != null)
						{
							sendMsg.SessionIds.Add(matchedPlayer.SessionId);
							sendMsg.SessionIds.Add(packet.SessionId);

							sendContent.Target = matchedPlayer.Name;
						}

						break;
				}

				sendMsg.Payload = sendContent.ToByteString();

				await m_MessageQueueService.PublishAsync("connect.send", sendMsg.ToByteArray());
			}));

			subscriptions.Add(await m_MessageQueueService.SubscribeAsync("chat.player.list", "player.list", async (sender, args) =>
			{
				using var scope = m_ServiceProvider.CreateScope();

				var packet = QueuePacket.Parser.ParseFrom(args.Message.Data);

				m_Logger.LogInformation($"chat.player.list => Receive packet from {packet.SessionId}");

				var getPlayersService = scope.ServiceProvider.GetRequiredService<IQueryService<GetPlayerQuery, PlayerInfo>>();
				var roomListService = scope.ServiceProvider.GetRequiredService<IQueryService<RoomListQuery, string>>();

				var responseSessionIds = roomListService.QueryAsync(new RoomListQuery
				{
					Room = "test"
				});

				var playersContent = new PlayerList();

				var allPlayers = getPlayersService.QueryAsync(new GetPlayerQuery
				{
					SessionIds = await responseSessionIds.ToArrayAsync().ConfigureAwait(false)
				});

				playersContent.Players.AddRange(allPlayers.Select(player => player.Name).ToEnumerable());

				var sendMsg = new SendPacket
				{
					Subject = "chat.room.list",
					Payload = playersContent.ToByteString()
				};

				sendMsg.SessionIds.Add(packet.SessionId);

				await m_MessageQueueService.PublishAsync("connect.send", sendMsg.ToByteArray());
			}));

			stoppingToken.Register(() =>
			{
				foreach (var sub in subscriptions)
					sub.Dispose();
			});
		}
	}
}
