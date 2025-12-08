namespace LuaVM.Tests
{
	using Burst;
	using NUnit.Framework;
	using Unity.Collections;

	[TestFixture]
	public class BurstOperationQueueTests
	{
		struct TestOperation
		{
			public int Id;
			public float Value;
		}

		[Test]
		public void Create_IsCreated()
		{
			var queue = new BurstOperationQueue<TestOperation>(16, Allocator.Persistent);

			Assert.IsTrue(queue.IsCreated);
			Assert.AreEqual(0, queue.Length);

			queue.Dispose();
		}

		[Test]
		public void Add_IncreasesLength()
		{
			var queue = new BurstOperationQueue<TestOperation>(16, Allocator.Persistent);

			queue.Add(new TestOperation { Id = 1, Value = 1.5f });
			queue.Add(new TestOperation { Id = 2, Value = 2.5f });

			Assert.AreEqual(2, queue.Length);

			queue.Dispose();
		}

		[Test]
		public void Indexer_ReturnsCorrectItem()
		{
			var queue = new BurstOperationQueue<TestOperation>(16, Allocator.Persistent);

			queue.Add(new TestOperation { Id = 42, Value = 3.14f });
			queue.Add(new TestOperation { Id = 100, Value = 2.71f });

			Assert.AreEqual(42, queue[0].Id);
			Assert.AreEqual(3.14f, queue[0].Value, 0.001f);
			Assert.AreEqual(100, queue[1].Id);

			queue.Dispose();
		}

		[Test]
		public void Clear_ResetsLength()
		{
			var queue = new BurstOperationQueue<TestOperation>(16, Allocator.Persistent);

			queue.Add(new TestOperation { Id = 1, Value = 1.0f });
			queue.Add(new TestOperation { Id = 2, Value = 2.0f });
			queue.Clear();

			Assert.AreEqual(0, queue.Length);

			queue.Dispose();
		}

		[Test]
		public void Dispose_SetsIsCreatedFalse()
		{
			var queue = new BurstOperationQueue<TestOperation>(16, Allocator.Persistent);

			queue.Dispose();

			Assert.IsFalse(queue.IsCreated);
		}
	}
}
