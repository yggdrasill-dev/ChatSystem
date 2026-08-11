using Common.Chat;
using Common.Rooms;
using Common.Tests.Chat;
using Common.Tests.Rooms;

namespace E2E.Tests;

// **B0 那 20 條契約斷言在這裡對著真 Postgres 跑一次。** 那組斷言當初就是為這一刻寫的：
// 「換掉工廠方法就該通過」。in-memory 版仍然在 Common.Tests 跑（0.4 秒、不需要容器），
// 這裡是同一份基底類別的第二個實作。
//
// 跟 E2E 其他測試不同，這兩個類別**不驗任何接線**——不經過 WebSocket、不經過 NATS，只是借用
// 這個專案已經有的容器。放這裡的唯一理由是「需要真的資料庫」。
//
// **閘門是編譯期的，跟 [E2EFact] 那個執行期閘門不同。** 基底類別的測試是普通的 [Fact]（in-memory
// 版必須永遠跑），派生類別改不了它們的屬性，而 xunit v2 沒有動態 skip（Assert.Skip 是 v3 的
// 功能）。所以由 E2E.Tests.csproj 在 CHATSYSTEM_E2E != 1 時整份 Compile Remove 掉。
//
// 剩下的替代方案都更糟：讓工廠方法在閘門關著時回傳 in-memory store，會讓這兩個類別在沒有
// Docker 時**變成綠的卻什麼都沒驗到**；不設閘門則預設的 dotnet test 會紅。
[Collection(AppHostCollection.Name)]
public sealed class PostgresRoomStoreContractTests(AppHostFixture fixture) : RoomStoreContract, IAsyncLifetime
{
	private ChatDbProbe m_Probe = null!;

	public async Task InitializeAsync() => m_Probe = await fixture.ConnectChatDbAsync();

	public async Task DisposeAsync() => await m_Probe.DisposeAsync();

	protected override async ValueTask<IRoomStore> NewStoreAsync()
	{
		await m_Probe.ResetAsync();

		return m_Probe.Store;
	}
}

[Collection(AppHostCollection.Name)]
public sealed class PostgresRoomBanListContractTests(AppHostFixture fixture) : RoomBanListContract, IAsyncLifetime
{
	private ChatDbProbe m_Probe = null!;

	public async Task InitializeAsync() => m_Probe = await fixture.ConnectChatDbAsync();

	public async Task DisposeAsync() => await m_Probe.DisposeAsync();

	protected override async ValueTask<(IRoomStore Store, IRoomBanList Bans)> NewAsync()
	{
		await m_Probe.ResetAsync();

		return (m_Probe.Store, m_Probe.Bans);
	}

	// **不在契約裡的一條，只有 Postgres 給得起。** ADR-10 的「刪房把封鎖名單一起帶走」在每個實作上
	// 長得不一樣（這裡是 ON DELETE CASCADE，先前的 Redis 版是自己多刪一個 key），而 in-memory 的
	// 兩個替身是獨立物件、湊不出那個連動——所以它進不了 RoomBanListContract。
	//
	// 這一條接手先前 E2E 那條 RoomStore_DeleteTakesTheBanListWithIt：那時驗的是 Redis 版手動刪 key，
	// 現在驗的是 FK。**斷言一模一樣，機制換了。**
	[Fact]
	public async Task Delete_TakesTheBanListWithIt_ThroughTheForeignKey()
	{
		var (store, bans) = await NewAsync();

		Assert.True(await store.TryCreateAsync(new Room(
			"room-1",
			"Lobby",
			null,
			"owner",
			DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000))));

		await bans.BanAsync("room-1", "banned-user");

		Assert.True(await store.TryDeleteAsync("room-1"));
		Assert.False(await bans.IsBannedAsync("room-1", "banned-user"));
	}
}

[Collection(AppHostCollection.Name)]
public sealed class PostgresChatMessageStoreContractTests(AppHostFixture fixture)
	: ChatMessageStoreContract, IAsyncLifetime
{
	private ChatDbProbe m_Probe = null!;

	public async Task InitializeAsync() => m_Probe = await fixture.ConnectChatDbAsync();

	public async Task DisposeAsync() => await m_Probe.DisposeAsync();

	protected override async ValueTask<(IChatMessageStore Messages, IRoomStore Rooms)> NewAsync()
	{
		await m_Probe.ResetAsync();

		return (m_Probe.Messages, m_Probe.Store);
	}

	// **ADR-10 在這一條閉合。** 關房＝刪房，訊息隨 FK 一起消失——在此之前訊息活在
	// InMemoryChatMessageStore 裡，刪房會留下孤兒訊息（無害，因為它們本來就 process 重啟即消失，
	// 但那是「不能上線」的第三條）。
	//
	// 跟上面那條封鎖名單的一樣，**進不了契約**：in-memory 的訊息 store 與房間 store 是兩個獨立
	// 物件，湊不出 FK 的連動。這是「只有真資料庫給得起的性質」該待的地方。
	[Fact]
	public async Task Delete_TakesTheMessagesWithIt_ThroughTheForeignKey()
	{
		var (messages, rooms) = await NewAsync();

		Assert.True(await rooms.TryCreateAsync(new Room(
			"room-1",
			"Lobby",
			null,
			"owner",
			DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000))));

		Assert.True(await messages.TryAppendAsync(new ChatMessage(
			"room-1",
			100,
			"alice",
			"Alice",
			"hi",
			DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000))));

		Assert.True(await rooms.TryDeleteAsync("room-1"));

		var page = await messages.GetPageAsync("room-1", 0, 10);

		Assert.Empty(page.Messages);
	}
}
