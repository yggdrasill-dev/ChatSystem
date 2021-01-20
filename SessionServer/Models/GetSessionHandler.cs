using System;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using NATS.Client;

namespace SessionServer.Models
{
	public class GetSessionHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IGetService<GetPlayerBySessionIdQuery, Registration> m_PlayerService;

		public GetSessionHandler(
			IMessageQueueService messageQueueService,
			IGetService<GetPlayerBySessionIdQuery, Registration> playerService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_PlayerService = playerService ?? throw new ArgumentNullException(nameof(playerService));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var query = GetPlayerRequest.Parser.ParseFrom(msg.Data);

			var playerReg = await m_PlayerService.GetAsync(new GetPlayerBySessionIdQuery
			{
				SessionId = query.SessionId
			}).ConfigureAwait(false);


			var reply = new GetPlayerResponse();

			if (playerReg != null)
			{

				reply.Player = new PlayerInfo
				{
					SessionId = playerReg.SessionId,
					ConnectorId = playerReg.ConnectorId,
					Name = playerReg.Name
				};

				await m_MessageQueueService.PublishAsync(msg.Reply, reply.ToByteArray()).ConfigureAwait(false);
			}
			else
			{
				reply.Player = null;

				await m_MessageQueueService.PublishAsync(msg.Reply, reply.ToByteArray()).ConfigureAwait(false);
			}
		}
	}
}
