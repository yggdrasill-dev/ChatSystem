using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using NATS.Client;

namespace SessionServer.Models.Handlers;

public class ActiveSessionHandler(ISessionRepository sessionRepository) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		var request = ActiveSessionsRequest.Parser.ParseFrom(msg.Data);

		await Task.WhenAll(
			request.SessionIds.Select(
				sessionId => sessionRepository
					.ActiveSessionAsync(sessionId)
					.AsTask()))
			.ConfigureAwait(false);
	}
}
