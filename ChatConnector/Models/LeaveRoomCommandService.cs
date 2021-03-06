﻿using System;
using System.Threading.Tasks;
using Common;
using Chat.Protos;
using Google.Protobuf;

namespace ChatConnector.Models
{
	public class LeaveRoomCommandService : ICommandService<LeaveRoomCommand>
	{
		private readonly IMessageQueueService m_MessageQueueService;

		public LeaveRoomCommandService(IMessageQueueService messageQueueService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}

		public async ValueTask ExecuteAsync(LeaveRoomCommand command)
		{
			var request = new LeaveRoomRequest
			{
				SessionId = command.SessionId,
				Room = command.Room ?? string.Empty
			};

			await m_MessageQueueService
				.RequestAsync("room.leave", request.ToByteArray())
				.ConfigureAwait(false);
		}
	}
}
