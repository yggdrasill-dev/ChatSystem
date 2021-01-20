using StackExchange.Redis;

namespace RoomServer.Models
{
	public class RedisConnectionFactory
	{
		public IDatabase Database { get; }

		public IServer Server { get; }

		public RedisConnectionFactory()
		{
			var connection = ConnectionMultiplexer.Connect("localhost:6379");

			Database = connection.GetDatabase();

			Server = connection.GetServer("localhost:6379");
		}
	}
}
