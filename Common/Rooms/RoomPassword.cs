using System.Security.Cryptography;

namespace Common.Rooms;

// 房間密碼的雜湊。存雜湊而不是可還原的形式，房主忘記就重設（room-layer.md ADR-6）。
//
// 用 PBKDF2 而不是單純的 SHA256：房間密碼是人選的，會很短、會重複使用，沒有 KDF 的話
// 一張彩虹表就破了。迭代次數對「加入房間」這種低頻操作來說不痛。
internal static class RoomPassword
{
	private const int SaltSize = 16;
	private const int HashSize = 32;
	private const int Iterations = 100_000;

	// 每間房自己的 salt，跟雜湊一起存在同一個欄位裡。
	public static string Hash(string password)
	{
		var salt = RandomNumberGenerator.GetBytes(SaltSize);
		var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

		return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
	}

	public static bool Verify(string password, string? storedHash)
	{
		// 公開房：任何密碼都算過（包含沒帶密碼）。
		if (string.IsNullOrEmpty(storedHash))
			return true;

		var parts = storedHash.Split(':');

		// 存的東西壞掉時一律拒絕，不是一律放行——失敗要往安全的那邊倒。
		if (parts.Length != 2)
			return false;

		byte[] salt;
		byte[] expected;

		try
		{
			salt = Convert.FromBase64String(parts[0]);
			expected = Convert.FromBase64String(parts[1]);
		}
		catch (FormatException)
		{
			return false;
		}

		var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);

		// 固定時間比較：避免用回應時間逐位元猜出雜湊。
		return CryptographicOperations.FixedTimeEquals(actual, expected);
	}
}
