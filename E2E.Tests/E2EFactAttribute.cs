namespace E2E.Tests;

// 這一組測試會啟動整個 AppHost（4 個服務 + 3 個容器），而且寬限期那一條要等真的 40 秒，
// 所以預設不跑——不然 `dotnet test` 從 2 秒變成 2 分鐘，沒有人會願意常跑。
//
// 開啟方式：
//   pwsh:  $env:CHATSYSTEM_E2E = '1'; dotnet test E2E.Tests
//   bash:  CHATSYSTEM_E2E=1 dotnet test E2E.Tests
//
// 需要 Docker Desktop 在跑。
public sealed class E2EFactAttribute : FactAttribute
{
	// 契約測試的基底類別用的是普通的 [Fact]（in-memory 版必須永遠跑），所以那兩個 Postgres 殼
	// 改不了屬性，只能在工廠方法裡 Assert.SkipUnless。理由字串共用這一份。
	internal const string SkipReason = "需要 CHATSYSTEM_E2E=1（會啟動整個 AppHost，需要 Docker）。";

	public E2EFactAttribute()
	{
		if (!Enabled)
			Skip = SkipReason;
	}

	internal static bool Enabled => Environment.GetEnvironmentVariable("CHATSYSTEM_E2E") == "1";
}
