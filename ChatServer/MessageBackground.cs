using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace ChatServer
{
	public class MessageBackground : BackgroundService
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly ILogger<MessageBackground> m_Logger;

		public MessageBackground(IMessageQueueService messageQueueService, ILogger<MessageBackground> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new System.ArgumentNullException(nameof(messageQueueService));
			m_Logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			var subscriptions = new List<IDisposable>();

			subscriptions.Add(await m_MessageQueueService.SubscribeAsync("chat.send", "chat", async (sender, args) =>
			{
				var packet = QueuePacket.Parser.ParseFrom(args.Message.Data);

				m_Logger.LogInformation($"Receive packet from {packet.SessionId}");

				var msg = ChatContent.Parser.ParseFrom(packet.Payload);
				m_Logger.LogInformation($"Scope: {msg.Scope}, Target: {msg.Target}, Message: {msg.Message}");

				var getPlayerQuery = new PlayerQuery
				{
					SessionId = packet.SessionId
				};

				var queryReply = await m_MessageQueueService
					.RequestAsync("session.get", getPlayerQuery.ToByteArray())
					.ConfigureAwait(false);

				var playerInfo = PlayerRegistration.Parser.ParseFrom(queryReply.Data);

				if (playerInfo.SessionId != packet.SessionId)
					return;

				var sendContent = new ChatContent
				{
					Scope = msg.Scope,
					Target = msg.Target,
					From = playerInfo.Name,
					Message = msg.Message
				};

				var sendMsg = new SendPacket
				{
					Subject = "chat.receive",
					Payload = sendContent.ToByteString()
				};

				switch (msg.Scope)
				{
					case Scope.Room:
						var roomQuery = new RoomSessionsRequest
						{
							Room = "test"
						};

						var result = await m_MessageQueueService.RequestAsync("room.query", roomQuery.ToByteArray());
						var queryResponse = RoomSessionsResponse.Parser.ParseFrom(result.Data);

						sendMsg.SessionIds.AddRange(queryResponse.SessionIds);
						break;

					case Scope.Person:
						sendMsg.SessionIds.Add(packet.SessionId);
						break;
				}

				await m_MessageQueueService.PublishAsync("connect.send", sendMsg.ToByteArray());
			}));

			subscriptions.Add(await m_MessageQueueService.SubscribeAsync("chat.player.list", "player.list", async (sender, args) =>
			{
				var packet = QueuePacket.Parser.ParseFrom(args.Message.Data);

				m_Logger.LogInformation($"chat.player.list => Receive packet from {packet.SessionId}");

				var roomQuery = new RoomSessionsRequest
				{
					Room = "test"
				};

				var result = await m_MessageQueueService.RequestAsync("room.query", roomQuery.ToByteArray());
				var playerQueryResponse = RoomSessionsResponse.Parser.ParseFrom(result.Data);
				var playersContent = new PlayerList();

				m_Logger.LogInformation(string.Join(", ", playerQueryResponse.SessionIds));
				foreach (var sessionId in playerQueryResponse.SessionIds)
				{
					var getPlayerQuery = new PlayerQuery
					{
						SessionId = sessionId
					};

					var queryReply = await m_MessageQueueService
						.RequestAsync("session.get", getPlayerQuery.ToByteArray())
						.ConfigureAwait(false);

					var playerInfo = PlayerRegistration.Parser.ParseFrom(queryReply.Data);

					playersContent.Players.Add(playerInfo.Name);
					m_Logger.LogInformation($"query player ({playerInfo.SessionId}, {playerInfo.Name})");
				}

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
