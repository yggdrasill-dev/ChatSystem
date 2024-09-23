using System.Threading.Tasks;
using Common;

namespace SessionServer.Models;

public class GetPlayerBySessionIdService(ISessionRepository sessionRepository) : IGetService<GetPlayerBySessionIdQuery, Registration?>
{
	public ValueTask<Registration?> GetAsync(GetPlayerBySessionIdQuery query)
	{
		return sessionRepository.GetRegistrationAsync(query.SessionId);
	}
}
