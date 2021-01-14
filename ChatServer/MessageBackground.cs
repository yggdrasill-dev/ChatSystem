using Chat.Protos;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChatServer
{
	public class MessageBackground : BackgroundService
	{
		private readonly IConnection m_Connection;
		private readonly ILogger<MessageBackground> m_Logger;

		public MessageBackground(ConnectionFactory connectionFactory, ILogger<MessageBackground> logger)
		{
			m_Connection = connectionFactory.CreateConnection();
			m_Logger = logger;
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken)
		{
			m_Connection.SubscribeAsync("chat.send", "chat", async (sender, args) =>
			{
				var packet = QueuePacket.Parser.ParseFrom(args.Message.Data);

				m_Logger.LogInformation($"Receive packet from {packet.SessionId}");

				var msg = ChatContent.Parser.ParseFrom(packet.Payload);
				m_Logger.LogInformation($"Scope: {msg.Scope}, Target: {msg.Target}, Message: {msg.Message}");

				var getPlayerQuery = new PlayerQuery
				{
					SessionId = packet.SessionId
				};

				var queryReply = await m_Connection
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
					Payload = sendContent.ToByteString()
				};
				sendMsg.SessionIds.Add(packet.SessionId);

				m_Connection.Publish("connect.send", sendMsg.ToByteArray());
			});

			return Task.CompletedTask;
		}
	}
}
