using System.Threading.Tasks;

namespace RoomServer.Models
{
	public interface IGetService<TQuery, TResult>
	{
		ValueTask<TResult> GetAsync(TQuery query);
	}
}
