using System.Security.Cryptography;

namespace Common.Identity;

// Session token 與登入 nonce 共用的產生方式。
//
// 兩者都是「不可猜測就夠」的不透明字串，不帶任何結構、不自我驗證——身分層刻意不用 JWT，
// 因為需要立即 revoke 的場景本來就得查表（identity-layer.md ADR-2）。
internal static class OpaqueToken
{
	// 64 個 hex 字元 = 32 bytes 的密碼學隨機值。
	public static string New() => RandomNumberGenerator.GetHexString(64, lowercase: true);
}
