using System.Threading.Tasks;

namespace Common
{
	public interface IGetService<TQuery, TResult>
	{
		ValueTask<TResult> GetAsync(TQuery query);
	}
}
