using System.Net.WebSockets;
using Chat.Protos;
using Common;
using Common.Connections;
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

// 對著真的 Kestrel WebSocket 驗證 receive loop 的行為，因為要測的正是分片訊息的累積過程，
// 用 test double 模擬 ReceiveAsync 沒辦法真實反映分片。
public class GatewayWebSocketEndpointTests
{
	[Fact]
	public async Task ReceiveLoop_ClosesWithMessageTooBig_WhenInboundMessageExceedsTheLimit()
	{
		var inbound = Substitute.For<IInboundMessageHandler>();
		var (app, wsUri) = await StartGatewayAsync(inbound);

		try
		{
			using var client = new ClientWebSocket();
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

			Assert.Equal(WebSocketCloseStatus.MessageTooBig, closeStatus);

			// 訊息從頭到尾沒有完整收完，不該有任何東西被交給上層
			await inbound.DidNotReceive().HandleAsync(
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
	public async Task ReceiveLoop_HandsPacketToInboundHandler_WhenMessageIsWithinTheLimit()
	{
		var received = new TaskCompletionSource<(string Subject, int PayloadLength)>();
		var inbound = Substitute.For<IInboundMessageHandler>();

		inbound
			.When(handler => handler.HandleAsync(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<ByteString>(),
				Arg.Any<CancellationToken>()))
			.Do(call => received.TrySetResult((call.ArgAt<string>(1), call.ArgAt<ByteString>(2).Length)));

		var (app, wsUri) = await StartGatewayAsync(inbound);

		try
		{
			using var client = new ClientWebSocket();
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

			Assert.Equal("test.subject", result.Subject);
			Assert.Equal(200 * 1024, result.PayloadLength);
			Assert.Equal(WebSocketState.Open, client.State);
		}
		finally
		{
			await app.StopAsync();
		}
	}

	private static async Task<(WebApplication App, Uri WsUri)> StartGatewayAsync(IInboundMessageHandler inbound)
	{
		var builder = WebApplication.CreateBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls("http://127.0.0.1:0");

		builder.Services.AddSingleton(new GatewayNodeId("test-node"));
		builder.Services.AddSingleton<ConnectionRegistry>();
		builder.Services.AddSingleton(Substitute.For<IConnectionDirectory>());
		builder.Services.AddSingleton(Substitute.For<IConnectionEventPublisher>());
		builder.Services.AddSingleton<ConnectionLifecycle>();
		builder.Services.AddSingleton(inbound);

		var app = builder.Build();
		app.UseWebSockets();
		app.MapGatewayWebSocket("/ws");

		await app.StartAsync();

		var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
		var wsUri = new Uri(addressFeature.Addresses.First().Replace("http://", "ws://") + "/ws");

		return (app, wsUri);
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
