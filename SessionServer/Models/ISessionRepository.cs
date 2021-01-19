using System.Threading.Tasks;

namespace SessionServer.Models
{
	public interface ISessionRepository
	{
		ValueTask<Registration?> GetRegistrationAsync(string sessionId);

		ValueTask RegisterSessionAsync(Registration reg);

		ValueTask ActiveSessionAsync(string sessionId);

		ValueTask UnregisterSessionBySessionIdAsync(string sessionId);
	}
}
