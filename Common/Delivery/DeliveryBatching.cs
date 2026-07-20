namespace Common.Delivery;

// 依 payload 大小動態決定每則訊息最多帶幾個 connectionId，避免序列化後超過 NATS 預設 1MB payload 上限。
// 只處理「connection_ids 數量龐大（上萬筆）」這個情境；payload 本身逼近或超過上限是另一個問題，不在此處理範圍內。
public static class DeliveryBatching
{
	// 保守值，低於 NATS 預設 1MB payload 上限，留緩衝給 subject/protobuf 額外開銷；未經負載測試驗證。
	private const int MaxMessageBytes = 900_000;

	// ConnectionId 目前是 Guid("N") 格式（32 hex 字元），加上 protobuf repeated string 欄位的 tag/length overhead，這裡取保守估計。
	private const int ApproxBytesPerConnectionId = 40;

	public static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyCollection<string> connectionIds, int payloadLength)
	{
		var batchSize = Math.Max(1, (MaxMessageBytes - payloadLength) / ApproxBytesPerConnectionId);

		foreach (var batch in connectionIds.Chunk(batchSize))
			yield return batch;
	}
}
