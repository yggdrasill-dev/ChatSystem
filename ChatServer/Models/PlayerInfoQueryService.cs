using System;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatServer.Models
{
	public class PlayerInfoQueryService : IGetService<PlayerInfoQuery, PlayerInfo?>
	{
		private readonly IMessageQueueService m_MessageQueueService;

		public PlayerInfoQueryService(
			IMessageQueueService messageQueueService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}

		public async ValueTask<PlayerInfo?> GetAsync(PlayerInfoQuery query)
		{
			var getPlayerQuery = new GetPlayerRequest
			{
				SessionId = query.SessionId
			};

			var queryReply = await m_MessageQueueService
				.RequestAsync("session.get", getPlayerQuery.ToByteArray())
				.ConfigureAwait(false);

			var data = GetPlayerResponse.Parser.ParseFrom(queryReply.Data);

			if (data.Player != null)
				return new PlayerInfo(data.Player.SessionId, data.Player.ConnectorId, data.Player.Name);
			else
				return null;
		}
	}
}
