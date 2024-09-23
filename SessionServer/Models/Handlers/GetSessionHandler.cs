using System;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using NATS.Client;

namespace SessionServer.Models.Handlers;

public class GetSessionHandler(
	IMessageQueueService messageQueueService,
	IGetService<GetPlayerBySessionIdQuery, Registration> playerService) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		var query = GetPlayerRequest.Parser.ParseFrom(msg.Data);

		var playerReg = await playerService.GetAsync(new GetPlayerBySessionIdQuery
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

			await messageQueueService.PublishAsync(msg.Reply, reply.ToByteArray()).ConfigureAwait(false);
		}
		else
		{
			reply.Player = null;

			await messageQueueService.PublishAsync(msg.Reply, reply.ToByteArray()).ConfigureAwait(false);
		}
	}
}
