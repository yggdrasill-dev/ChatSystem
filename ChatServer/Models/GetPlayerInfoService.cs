using System;
using System.Collections.Generic;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatServer.Models
{
	public class GetPlayerInfoService : IQueryService<GetPlayerQuery, PlayerInfo>
	{
		private readonly IMessageQueueService m_MessageQueueService;

		public GetPlayerInfoService(IMessageQueueService messageQueueService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}

		public async IAsyncEnumerable<PlayerInfo> QueryAsync(GetPlayerQuery query)
		{
			foreach (var sessionId in query.SessionIds)
			{
				var getPlayerQuery = new PlayerQuery
				{
					SessionId = sessionId
				};

				var queryReply = await m_MessageQueueService
					.RequestAsync("session.get", getPlayerQuery.ToByteArray())
					.ConfigureAwait(false);

				var data = PlayerRegistration.Parser.ParseFrom(queryReply.Data);

				yield return new PlayerInfo(data.SessionId, data.Name);
			}
		}
	}
}
