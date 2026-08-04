using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using Chat.Protos;
using Common;
using Common.Connections;
using Common.Identity;
using Gateway.Models;
using Gateway.Services;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Gateway.Tests.Services;

// 對著真的 Kestrel WebSocket 驗證 handshake 與 receive loop 的行為：要測的正是分片訊息的
// 累積過程與 handshake 階段的 HTTP 狀態碼，兩者都沒辦法用 test double 真實反映。
public class GatewayWebSocketEndpointTests
{
	private const string AllowedOrigin = "http://localhost:5173";
	private const string Principal = "user-1";

	// 伺服器端記到 Error 的 log。每個測試都斷言它是空的——不然「endpoint 自己炸掉」會偽裝成
	// 「狀態碼跟預期不符」，而真正的原因（例如 minimal API 的參數繫結在 routing 階段就丟例外）
	// 完全看不到。這個 harness 本身就是踩過那個坑之後加的。
	private readonly ConcurrentBag<string> m_ServerErrors = [];

	[Fact]
	public async Task Handshake_IsForbidden_WhenTheOriginIsNotOnTheAllowlist()
	{
		var (app, wsUri, _) = await StartGatewayAsync(Substitute.For<IInboundMessageHandler>());

		try
		{
			// WebSocket handshake 不受 CORS 約束，所以攻擊者的頁面本來就連得上——
			// 擋下它的只有這個 Origin 檢查（CSWSH）
			var status = await ConnectAndGetStatusAsync(wsUri, origin: "http://evil.example");

			Assert.Empty(m_ServerErrors);
			Assert.Equal(HttpStatusCode.Forbidden, status);
		}
		finally
		{
			await app.StopAsync();
		}
	}

	[Fact]
	public async Task Handshake_IsForbidden_WhenThereIsNoOrigin()
	{
		var (app, wsUri, _) = await StartGatewayAsync(Substitute.For<IInboundMessageHandler>());

		try
		{
			// 現階段唯一的 client 是瀏覽器，而瀏覽器一定會帶 Origin
			var status = await ConnectAndGetStatusAsync(wsUri, origin: null);

			Assert.Empty(m_ServerErrors);
			Assert.Equal(HttpStatusCode.Forbidden, status);
		}
		finally
		{
			await app.StopAsync();
		}
	}

	[Fact]
	public async Task Handshake_IsUnauthorized_WhenThereIsNoValidSession()
	{
		var (app, wsUri, authenticator) = await StartGatewayAsync(Substitute.For<IInboundMessageHandler>());
		authenticator.ResolveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((string?)null);

		try
		{
			var status = await ConnectAndGetStatusAsync(wsUri, AllowedOrigin);

			// 驗證發生在 accept 之前，所以 client 拿到的是乾淨的 401，
			// 不是「連上了又莫名被踢」
			Assert.Empty(m_ServerErrors);
			Assert.Equal(HttpStatusCode.Unauthorized, status);
		}
		finally
		{
			await app.StopAsync();
		}
	}

	[Fact]
	public async Task ReceiveLoop_ClosesWithMessageTooBig_WhenInboundMessageExceedsTheLimit()
	{
		var inbound = Substitute.For<IInboundMessageHandler>();
		var (app, wsUri, _) = await StartGatewayAsync(inbound);

		try
		{
			using var client = new ClientWebSocket();
			client.Options.SetRequestHeader("Origin", AllowedOrigin);
			await client.ConnectAsync(wsUri, CancellationToken.None);

			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

			// 模擬「永不結束的分片訊息」這個攻擊：每片 32 KB，全部 endOfMessage = false。
			// 累積超過 256 KB 時就該被攔下來，不必等訊息結束、也不必是合法的 Packet。
			var chunk = new byte[32 * 1024];

			for (var i = 0; i < 12; i++)
			{
				try
				{
					await client.SendAsync(chunk, WebSocketMessageType.Binary, false, cts.Token);
				}
				catch (WebSocketException)
				{
					break; // 伺服器已經關了，剩下的不用送
				}
			}

			var closeStatus = await ReceiveUntilCloseAsync(client, cts.Token);

			Assert.Empty(m_ServerErrors);
			Assert.Equal(WebSocketCloseStatus.MessageTooBig, closeStatus);

			// 訊息從頭到尾沒有完整收完，不該有任何東西被交給上層
			await inbound.DidNotReceive().HandleAsync(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<ByteString>(),
				Arg.Any<CancellationToken>());
		}
		finally
		{
			await app.StopAsync();
		}
	}

	[Fact]
	public async Task ReceiveLoop_HandsPacketAndPrincipalToInboundHandler_WhenMessageIsWithinTheLimit()
	{
		var received = new TaskCompletionSource<(string Principal, string Subject, int PayloadLength)>();
		var inbound = Substitute.For<IInboundMessageHandler>();

		inbound
			.When(handler => handler.HandleAsync(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<ByteString>(),
				Arg.Any<CancellationToken>()))
			.Do(call => received.TrySetResult((
				call.ArgAt<string>(1),
				call.ArgAt<string>(2),
				call.ArgAt<ByteString>(3).Length)));

		var (app, wsUri, _) = await StartGatewayAsync(inbound);

		try
		{
			using var client = new ClientWebSocket();
			client.Options.SetRequestHeader("Origin", AllowedOrigin);
			await client.ConnectAsync(wsUri, CancellationToken.None);

			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

			// 200 KB，在上限之內但大到一定會分片，確認分片累積本身沒被大小檢查誤殺
			var packet = new Packet
			{
				Subject = "test.subject",
				Payload = ByteString.CopyFrom(new byte[200 * 1024])
			};

			await client.SendAsync(packet.ToByteArray(), WebSocketMessageType.Binary, true, cts.Token);

			var result = await received.Task.WaitAsync(cts.Token);

			// principal 隨每則訊息往上走，上層因此不必查「這條連線是誰」
			Assert.Empty(m_ServerErrors);
			Assert.Equal(Principal, result.Principal);
			Assert.Equal("test.subject", result.Subject);
			Assert.Equal(200 * 1024, result.PayloadLength);
			Assert.Equal(WebSocketState.Open, client.State);
		}
		finally
		{
			await app.StopAsync();
		}
	}

	private async Task<(WebApplication App, Uri WsUri, IConnectionAuthenticator Authenticator)> StartGatewayAsync(
		IInboundMessageHandler inbound)
	{
		var builder = WebApplication.CreateBuilder();
		builder.Logging.ClearProviders();
		builder.Logging.AddProvider(new CapturingLoggerProvider(m_ServerErrors));
		builder.WebHost.UseUrls("http://127.0.0.1:0");

		var authenticator = Substitute.For<IConnectionAuthenticator>();
		authenticator.ResolveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Principal);

		builder.Services.AddSingleton(new GatewayNodeId("test-node"));
		builder.Services.AddSingleton(new AllowedOrigins([AllowedOrigin]));
		builder.Services.AddSingleton<ConnectionRegistry>();
		builder.Services.AddSingleton(Substitute.For<IConnectionDirectory>());
		builder.Services.AddSingleton(Substitute.For<IConnectionEventPublisher>());
		builder.Services.AddSingleton(authenticator);
		builder.Services.AddSingleton<ConnectionLifecycle>();
		builder.Services.AddSingleton(inbound);

		var app = builder.Build();
		app.UseWebSockets();
		app.MapGatewayWebSocket("/ws");

		await app.StartAsync();

		var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
		var wsUri = new Uri(addressFeature.Addresses.First().Replace("http://", "ws://") + "/ws");

		return (app, wsUri, authenticator);
	}

	// handshake 被拒時 ConnectAsync 會丟例外，狀態碼要靠 CollectHttpResponseDetails 才拿得到。
	private static async Task<HttpStatusCode?> ConnectAndGetStatusAsync(Uri wsUri, string? origin)
	{
		using var client = new ClientWebSocket();
		client.Options.CollectHttpResponseDetails = true;

		if (origin is not null)
			client.Options.SetRequestHeader("Origin", origin);

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

		await Assert.ThrowsAsync<WebSocketException>(async () => await client.ConnectAsync(wsUri, cts.Token));

		return client.HttpStatusCode;
	}

	private sealed class CapturingLoggerProvider(ConcurrentBag<string> errors) : ILoggerProvider
	{
		public ILogger CreateLogger(string categoryName) => new CapturingLogger(errors);

		public void Dispose()
		{
		}

		private sealed class CapturingLogger(ConcurrentBag<string> errors) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

			public void Log<TState>(
				LogLevel logLevel,
				EventId eventId,
				TState state,
				Exception? exception,
				Func<TState, Exception?, string> formatter)
			{
				if (logLevel >= LogLevel.Error)
					errors.Add($"{formatter(state, exception)}{Environment.NewLine}{exception}");
			}
		}
	}

	private static async Task<WebSocketCloseStatus?> ReceiveUntilCloseAsync(ClientWebSocket client, CancellationToken cancellationToken)
	{
		var buffer = new byte[8192];

		while (true)
		{
			var result = await client.ReceiveAsync(buffer, cancellationToken);

			if (result.MessageType == WebSocketMessageType.Close)
				return result.CloseStatus;
		}
	}
}
