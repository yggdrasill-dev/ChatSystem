using System;
using System.Collections.Generic;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatServer.Models
{
	public class PlayerInfoQueryService : IQueryService<GetPlayerQuery, PlayerInfo>
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly ICommandService<LeaveRoomCommand> m_LeaveRoomService;

		public PlayerInfoQueryService(
			IMessageQueueService messageQueueService,
			ICommandService<LeaveRoomCommand> leaveRoomService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_LeaveRoomService = leaveRoomService ?? throw new ArgumentNullException(nameof(leaveRoomService));
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

				if (sessionId != data.SessionId)
					await m_LeaveRoomService.ExecuteAsync(new LeaveRoomCommand
					{
						SessionId = sessionId,
						Room = "test"
					}).ConfigureAwait(false);
				else
					yield return new PlayerInfo(data.SessionId, data.ConnectorId, data.Name);
			}
		}
	}
}
