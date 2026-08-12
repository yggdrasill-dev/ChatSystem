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

// 同一組限流的斷言在 E2E.Tests 對著真 Redis 再跑一次（RedisChatRateLimiterContractTests）。
//
// **「計數跨複本」那一條不在契約裡**：兩個 InMemoryChatRateLimiter 是兩個字典，那條斷言在這邊
// 必定紅——而它正是換 Redis 的全部理由，所以它是 Redis 派生那邊獨有的一條。跟「刪房把訊息一起
// 帶走」進不了 ChatMessageStoreContract 是同一個道理。
public sealed class InMemoryChatRateLimiterContractTests : ChatRateLimiterContract
{
	protected override ValueTask<(IChatRateLimiter Limiter, AdvanceableClock Clock)> NewAsync(ChatRateLimit limit)
	{
		var clock = NewClock();

		return ValueTask.FromResult<(IChatRateLimiter, AdvanceableClock)>(
			(new InMemoryChatRateLimiter(clock, limit), clock));
	}
}
