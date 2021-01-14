using System.Collections.Generic;

namespace RoomServer.Models
{
	public interface IQueryService<TQuery, TResult>
	{
		IAsyncEnumerable<TResult> QueryAsync(TQuery query);
	}
}
