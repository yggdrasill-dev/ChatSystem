using Common.Delivery;

namespace Common.Tests.Delivery;

public class DeliveryBatchingTests
{
	[Fact]
	public void Chunk_ReturnsSingleBatch_WhenConnectionIdsAreFew()
	{
		var connectionIds = new[] { "conn-a", "conn-b", "conn-c" };

		var batches = DeliveryBatching.Chunk(connectionIds, payloadLength: 0).ToArray();

		var batch = Assert.Single(batches);
		Assert.Equal(connectionIds, batch);
	}

	[Fact]
	public void Chunk_SplitsIntoMultipleBatches_WhenConnectionIdsAreMany()
	{
		var connectionIds = Enumerable.Range(0, 30_000).Select(i => $"conn-{i}").ToArray();

		var batches = DeliveryBatching.Chunk(connectionIds, payloadLength: 0).ToArray();

		Assert.True(batches.Length > 1, "Expected a large connection id list to be split across multiple batches.");
		Assert.Equal(connectionIds, batches.SelectMany(b => b));
	}

	[Fact]
	public void Chunk_ProducesMoreBatches_WhenPayloadIsLarger()
	{
		var connectionIds = Enumerable.Range(0, 20_000).Select(i => $"conn-{i}").ToArray();

		var smallPayloadBatches = DeliveryBatching.Chunk(connectionIds, payloadLength: 0).ToArray();
		var largePayloadBatches = DeliveryBatching.Chunk(connectionIds, payloadLength: 800_000).ToArray();

		Assert.True(
			largePayloadBatches.Length > smallPayloadBatches.Length,
			$"Expected a larger payload to shrink the batch size (more batches). Small payload: {smallPayloadBatches.Length}, large payload: {largePayloadBatches.Length}.");
		Assert.Equal(connectionIds, largePayloadBatches.SelectMany(b => b));
	}

	[Fact]
	public void Chunk_FallsBackToOneConnectionIdPerBatch_WhenPayloadAloneExceedsBudget()
	{
		var connectionIds = new[] { "conn-a", "conn-b", "conn-c" };

		var batches = DeliveryBatching.Chunk(connectionIds, payloadLength: 2_000_000).ToArray();

		Assert.Equal(connectionIds.Length, batches.Length);
		Assert.All(batches, batch => Assert.Single(batch));
	}
}
