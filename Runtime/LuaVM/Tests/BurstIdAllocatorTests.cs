namespace LuaVM.Tests
{
	using Burst;
	using NUnit.Framework;

	[TestFixture]
	public class BurstIdAllocatorTests
	{
		[SetUp]
		public void SetUp()
		{
			BurstIdAllocator.Reset();
		}

		[Test]
		public void Allocate_ReturnsSequentialIds()
		{
			var id1 = BurstIdAllocator.Allocate();
			var id2 = BurstIdAllocator.Allocate();
			var id3 = BurstIdAllocator.Allocate();

			Assert.AreEqual(1, id1);
			Assert.AreEqual(2, id2);
			Assert.AreEqual(3, id3);
		}

		[Test]
		public void Current_ReturnsLastAllocatedId()
		{
			BurstIdAllocator.Allocate();
			BurstIdAllocator.Allocate();

			Assert.AreEqual(2, BurstIdAllocator.Current);
		}

		[Test]
		public void Reset_StartsFromOne()
		{
			BurstIdAllocator.Allocate();
			BurstIdAllocator.Allocate();
			BurstIdAllocator.Reset();

			var id = BurstIdAllocator.Allocate();
			Assert.AreEqual(1, id);
		}

		[Test]
		public void SyncMinimum_UpdatesWhenBelowMinimum()
		{
			BurstIdAllocator.Allocate(); // 1
			BurstIdAllocator.SyncMinimum(100);

			var nextId = BurstIdAllocator.Allocate();
			Assert.AreEqual(101, nextId);
		}

		[Test]
		public void SyncMinimum_DoesNotUpdateWhenAboveMinimum()
		{
			for (var i = 0; i < 50; i++)
				BurstIdAllocator.Allocate();

			BurstIdAllocator.SyncMinimum(10);

			var nextId = BurstIdAllocator.Allocate();
			Assert.AreEqual(51, nextId);
		}
	}
}
