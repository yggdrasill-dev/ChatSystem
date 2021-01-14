namespace ChatConnector.Models
{
	public struct RegisterSessionCommand
	{
		public string SessionId { get; set; }

		public string ConnectorId { get; set; }

		public string Name { get; set; }
	}
}
