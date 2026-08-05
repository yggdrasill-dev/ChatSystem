using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json.Serialization;
using Chat.Protos;
using Common.Identity;
using Google.Protobuf;

namespace E2E.Tests;

// 一個「使用者」：走完 WebBff 的登入拿到 session cookie，再用它跟 Gateway 建立 WebSocket。
// 刻意只走系統的公開表面（HTTP + WebSocket），不碰任何內部服務——這樣它驗到的就是使用者
// 真正會走的那條路。
internal sealed class ChatClient : IAsyncDisposable
{
	// Gateway 的 Origin allowlist 是 appsettings.Development.json 裡的固定字面值，跟 Aspire 這次
	// 分配給 web-bff 的實際 port 無關，所以這裡直接用清單裡的值。
	private const string AllowedOrigin = "http://localhost:5150";

	private static readonly TimeSpan _DefaultTimeout = TimeSpan.FromSeconds(10);

	private readonly ClientWebSocket m_Socket;
	private readonly CancellationTokenSource m_Stopping = new();
	private readonly List<Packet> m_Received = [];
	private readonly Task m_ReceiveLoop;

	private ChatClient(string userId, ClientWebSocket socket)
	{
		UserId = userId;
		m_Socket = socket;
		m_ReceiveLoop = Task.Run(ReceiveLoopAsync);
	}

	public string UserId { get; }

	public WebSocketState State => m_Socket.State;

	public static async Task<ChatClient> ConnectAsync(AppHostFixture fixture, string userId)
	{
		var sessionToken = await LoginAsync(fixture, userId).ConfigureAwait(false);

		return await ConnectAsync(fixture, userId, sessionToken).ConfigureAwait(false);
	}

	// 重連用：同一個 session cookie 再開一條連線。分成兩個多載是因為 Supersede 與寬限期
	// 兩條測試都需要「同一個身分、不同連線」，而重新登入會拿到不同的 session。
	public static async Task<ChatClient> ConnectAsync(AppHostFixture fixture, string userId, string sessionToken)
	{
		var socket = new ClientWebSocket();
		socket.Options.SetRequestHeader("Origin", AllowedOrigin);
		socket.Options.SetRequestHeader("Cookie", $"{SessionCookie.Name}={sessionToken}");

		var wsUri = new Uri("ws://" + new Uri(fixture.GatewayHttp, "/ws").Authority + "/ws");

		using var timeout = new CancellationTokenSource(_DefaultTimeout);
		await socket.ConnectAsync(wsUri, timeout.Token).ConfigureAwait(false);

		return new ChatClient(userId, socket);
	}

	public static async Task<string> LoginAsync(AppHostFixture fixture, string userId)
	{
		using var http = fixture.CreateWebBffClient();

		var nonce = await http.GetFromJsonAsync<NonceResponse>("/login/nonce").ConfigureAwait(false)
			?? throw new InvalidOperationException("/login/nonce 沒有回傳內容。");

		// FakeIdTokenValidator 把 idToken 字串直接當成 userId，所以這裡的 idToken 就是身分本身。
		using var response = await http
			.PostAsJsonAsync("/login", new LoginRequest(userId, nonce.Nonce))
			.ConfigureAwait(false);

		response.EnsureSuccessStatusCode();

		// cookie 帶 Secure，所以不能靠 CookieContainer 自動回送（它只在 https 才送）。
		// 直接從 Set-Cookie 抽出 token 自己掛到 WebSocket 的 header 上，行為最明確。
		var setCookie = response.Headers.GetValues("Set-Cookie")
			.First(value => value.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal));

		return setCookie[(SessionCookie.Name.Length + 1)..].Split(';')[0];
	}

	public async Task SendAsync<TMessage>(string subject, TMessage message)
		where TMessage : IMessage
	{
		var packet = new Packet { Subject = subject, Payload = message.ToByteString() };

		using var timeout = new CancellationTokenSource(_DefaultTimeout);

		await m_Socket
			.SendAsync(packet.ToByteArray(), WebSocketMessageType.Binary, true, timeout.Token)
			.ConfigureAwait(false);
	}

	public async Task<T> ExpectAsync<T>(string subject, MessageParser<T> parser, TimeSpan? timeout = null)
		where T : IMessage<T>
	{
		var deadline = DateTime.UtcNow + (timeout ?? _DefaultTimeout);

		while (DateTime.UtcNow < deadline)
		{
			if (TryTake(subject, out var packet))
				return parser.ParseFrom(packet.Payload);

			await Task.Delay(25).ConfigureAwait(false);
		}

		throw new TimeoutException($"{UserId} 等不到 '{subject}'。這段時間收到的是：{Describe()}");
	}

	// 「不該收到」的斷言：等滿一個視窗，確認那個 subject 沒有出現。
	public async Task ExpectNothingAsync(string subject, TimeSpan window)
	{
		await Task.Delay(window).ConfigureAwait(false);

		if (TryTake(subject, out _))
			throw new InvalidOperationException($"{UserId} 不該收到 '{subject}'，但收到了。");
	}

	public void Clear()
	{
		lock (m_Received)
			m_Received.Clear();
	}

	// 模擬真實的斷線（關分頁、網路斷、行程被殺）而不是乾淨的 close handshake：只有前者會讓
	// 伺服器端的 HttpContext.RequestAborted 觸發，而那正是斷線事件那個 bug 的觸發條件。
	public void Abort() => m_Socket.Abort();

	public async ValueTask DisposeAsync()
	{
		await m_Stopping.CancelAsync().ConfigureAwait(false);

		m_Socket.Abort();
		m_Socket.Dispose();

		try
		{
			await m_ReceiveLoop.ConfigureAwait(false);
		}
		catch
		{
			// 收尾用，這裡的例外沒有意義。
		}

		m_Stopping.Dispose();
	}

	private bool TryTake(string subject, out Packet packet)
	{
		lock (m_Received)
		{
			var index = m_Received.FindIndex(candidate => candidate.Subject == subject);

			if (index < 0)
			{
				packet = null!;

				return false;
			}

			packet = m_Received[index];
			m_Received.RemoveAt(index);

			return true;
		}
	}

	private string Describe()
	{
		lock (m_Received)
			return m_Received.Count == 0
				? "（什麼都沒收到）"
				: string.Join(", ", m_Received.Select(packet => $"'{packet.Subject}'"));
	}

	private async Task ReceiveLoopAsync()
	{
		var buffer = new byte[8192];

		try
		{
			while (m_Socket.State == WebSocketState.Open && !m_Stopping.IsCancellationRequested)
			{
				using var message = new MemoryStream();
				WebSocketReceiveResult result;

				do
				{
					result = await m_Socket.ReceiveAsync(buffer, m_Stopping.Token).ConfigureAwait(false);

					if (result.MessageType == WebSocketMessageType.Close)
						return;

					message.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage);

				message.Position = 0;
				var packet = Packet.Parser.ParseFrom(message);

				lock (m_Received)
					m_Received.Add(packet);
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
		{
			// 連線被關掉/中止。是不是預期的由測試自己用 State 判斷。
		}
	}

	private sealed record LoginRequest(
		[property: JsonPropertyName("idToken")] string IdToken,
		[property: JsonPropertyName("nonce")] string Nonce);

	private sealed record NonceResponse([property: JsonPropertyName("nonce")] string Nonce);
}
