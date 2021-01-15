using System.Collections.Generic;

namespace Common
{
	public interface IQueryService<TQuery, TResult>
	{
		IAsyncEnumerable<TResult> QueryAsync(TQuery query);
	}
}
