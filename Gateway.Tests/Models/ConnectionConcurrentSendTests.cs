using System.Net.WebSockets;
using Gateway.Models;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Tests.Models;

// 回歸測試：WebSocket.SendAsync 官方文件明確寫「並行呼叫是 undefined behavior，
// 呼叫端要自己用 lock/semaphore 序列化」。這裡對著真的 Kestrel WebSocket（不是
// test double）驗證 Connection.SendAsync 在高併發下不會丟例外、也不會漏送/送錯。
public class ConnectionConcurrentSendTests
{
	private const int ConcurrentSends = 30;
	private const int PayloadSize = 256 * 1024;

	[Fact]
	public async Task SendAsync_SerializesConcurrentCalls_AgainstRealKestrelWebSocket()
	{
		var builder = WebApplication.CreateBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		var app = builder.Build();
		app.UseWebSockets();

		Exception? serverException = null;
		var serverDone = new TaskCompletionSource();

		app.Map("/ws", async (HttpContext context) =>
		{
			var socket = await context.WebSockets.AcceptWebSocketAsync();
			var connection = new Connection("test-conn", "user-1", socket);

			var buffer = new byte[64];
			await socket.ReceiveAsync(buffer, CancellationToken.None); // 等 client 的 "go"

			var gate = new TaskCompletionSource();
			var payload = ByteString.CopyFrom(new byte[PayloadSize]);

			var tasks = Enumerable.Range(0, ConcurrentSends)
				.Select(i => Task.Run(async () =>
				{
					await gate.Task;
					await connection.SendAsync($"subject-{i}", payload);
				}))
				.ToArray();

			await Task.Delay(50); // 讓所有 task 先卡在 gate 上，逼出真正的並行呼叫
			gate.TrySetResult();

			try
			{
				await Task.WhenAll(tasks);
			}
			catch (Exception ex)
			{
				serverException = ex;
			}

			serverDone.TrySetResult();
		});

		await app.StartAsync();

		try
		{
			var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
			var wsUri = new Uri(addressFeature.Addresses.First().Replace("http://", "ws://") + "/ws");

			using var client = new ClientWebSocket();
			client.Options.SetBuffer(PayloadSize + 1024, PayloadSize + 1024);
			await client.ConnectAsync(wsUri, CancellationToken.None);
			await client.SendAsync("go"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);

			var receivedSubjects = new HashSet<string>();
			var recvBuffer = new byte[PayloadSize + 1024];
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

			while (receivedSubjects.Count < ConcurrentSends)
			{
				using var stream = new MemoryStream();
				WebSocketReceiveResult result;

				do
				{
					result = await client.ReceiveAsync(recvBuffer, cts.Token);
					Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
					stream.Write(recvBuffer, 0, result.Count);
				}
				while (!result.EndOfMessage);

				stream.Position = 0;
				var packet = Chat.Protos.Packet.Parser.ParseFrom(stream);
				receivedSubjects.Add(packet.Subject);
			}

			await serverDone.Task;

			Assert.Null(serverException);
			Assert.Equal(ConcurrentSends, receivedSubjects.Count);
		}
		finally
		{
			await app.StopAsync();
		}
	}
}
