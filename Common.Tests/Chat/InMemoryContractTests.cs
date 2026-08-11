using Common.Chat;
using Common.Rooms;
using Common.Tests.Rooms;

namespace Common.Tests.Chat;

// 契約跑在 in-memory 實作上：不需要容器、跟其他單元測試一起跑完。
// 同一組斷言在 E2E.Tests 也跑一次，那邊接的是 Postgres（PostgresChatMessageStoreContractTests）。
//
// **兩個替身是各自獨立的物件**，所以這裡的 IRoomStore 只負責滿足「房間必須存在」這條前置條件，
// 湊不出 FK 的連動——「刪房把訊息一起帶走」因此進不了契約，它是 Postgres 派生那邊獨有的一條。
public sealed class InMemoryChatMessageStoreContractTests : ChatMessageStoreContract
{
	protected override ValueTask<(IChatMessageStore Messages, IRoomStore Rooms)> NewAsync() =>
		ValueTask.FromResult<(IChatMessageStore, IRoomStore)>(
			(new InMemoryChatMessageStore(), new InMemoryRoomStore()));
}
