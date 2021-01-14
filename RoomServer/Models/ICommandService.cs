using System.Threading.Tasks;

namespace RoomServer.Models
{
	public interface ICommandService<TCommand>
	{
		ValueTask ExecuteAsync(TCommand command);
	}
}
