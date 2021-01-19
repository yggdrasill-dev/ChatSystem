using System;
using System.Threading.Tasks;
using Common;

namespace SessionServer.Models
{
	public class GetPlayerBySessionIdService : IGetService<GetPlayerBySessionIdQuery, Registration?>
	{
		private readonly ISessionRepository m_SessionRepository;

		public GetPlayerBySessionIdService(ISessionRepository sessionRepository)
		{
			m_SessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
		}

		public ValueTask<Registration?> GetAsync(GetPlayerBySessionIdQuery query)
		{
			return m_SessionRepository.GetRegistrationAsync(query.SessionId);
		}
	}
}
