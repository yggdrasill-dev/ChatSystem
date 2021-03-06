﻿using System;
using System.Threading.Tasks;
using Common;

namespace RoomServer.Models
{
	public class GetRoomBySessionIdQueryService : IGetService<GetRoomBySessionIdQuery, string?>
	{
		private readonly IRoomRepository m_RoomRepository;

		public GetRoomBySessionIdQueryService(IRoomRepository roomRepository)
		{
			m_RoomRepository = roomRepository ?? throw new ArgumentNullException(nameof(roomRepository));
		}

		public ValueTask<string?> GetAsync(GetRoomBySessionIdQuery query)
		{
			return m_RoomRepository.GetRoomBySessionIdAsync(query.SessionId);
		}
	}
}
