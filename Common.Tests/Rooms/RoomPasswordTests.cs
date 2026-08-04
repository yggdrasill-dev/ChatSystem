using Common.Rooms;

namespace Common.Tests.Rooms;

public class RoomPasswordTests
{
	[Fact]
	public void Hash_ProducesADifferentValueEachTime_ForTheSamePassword()
	{
		// per-room salt：兩間房用同一個密碼，存下來的東西不能一樣，否則一張彩虹表打全部
		Assert.NotEqual(RoomPassword.Hash("same"), RoomPassword.Hash("same"));
	}

	[Fact]
	public void Hash_DoesNotContainThePassword()
	{
		Assert.DoesNotContain("s3cret", RoomPassword.Hash("s3cret"));
	}

	[Fact]
	public void Verify_AcceptsTheRightPassword_AndRejectsTheWrongOne()
	{
		var hash = RoomPassword.Hash("right");

		Assert.True(RoomPassword.Verify("right", hash));
		Assert.False(RoomPassword.Verify("wrong", hash));
		Assert.False(RoomPassword.Verify(string.Empty, hash));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void Verify_AcceptsAnything_ForAPublicRoom(string? storedHash)
	{
		// 公開房沒有密碼，client 帶什麼都算過（也可以什麼都不帶）
		Assert.True(RoomPassword.Verify("whatever", storedHash));
		Assert.True(RoomPassword.Verify(string.Empty, storedHash));
	}

	[Theory]
	[InlineData("no-separator")]
	[InlineData("too:many:parts")]
	[InlineData("not-base64:not-base64")]
	public void Verify_RejectsEverything_WhenTheStoredHashIsCorrupted(string storedHash)
	{
		// 存的東西壞掉時往安全的那邊倒：拒絕所有人，而不是放行所有人
		Assert.False(RoomPassword.Verify("whatever", storedHash));
	}
}
