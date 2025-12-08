namespace LuaVM.Tests
{
	using Burst;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Mathematics;

	[TestFixture]
	public class BurstIdLookupTests
	{
		[Test]
		public void Create_IsCreated()
		{
			var lookup = new BurstIdLookup<float3>(16, Allocator.Persistent);

			Assert.IsTrue(lookup.IsCreated);
			Assert.AreEqual(0, lookup.Count);

			lookup.Dispose();
		}

		[Test]
		public void Add_IncreasesCount()
		{
			var lookup = new BurstIdLookup<float3>(16, Allocator.Persistent);

			lookup.Add(1, new float3(1, 2, 3));
			lookup.Add(2, new float3(4, 5, 6));

			Assert.AreEqual(2, lookup.Count);

			lookup.Dispose();
		}

		[Test]
		public void TryGetValue_ReturnsCorrectValue()
		{
			var lookup = new BurstIdLookup<float3>(16, Allocator.Persistent);
			var expected = new float3(10, 20, 30);

			lookup.Add(42, expected);

			Assert.IsTrue(lookup.TryGetValue(42, out var result));
			Assert.AreEqual(expected.x, result.x);
			Assert.AreEqual(expected.y, result.y);
			Assert.AreEqual(expected.z, result.z);

			lookup.Dispose();
		}

		[Test]
		public void TryGetValue_ReturnsFalseForMissingKey()
		{
			var lookup = new BurstIdLookup<float3>(16, Allocator.Persistent);

			lookup.Add(1, new float3(1, 2, 3));

			Assert.IsFalse(lookup.TryGetValue(999, out _));

			lookup.Dispose();
		}

		[Test]
		public void ContainsKey_ReturnsCorrectResult()
		{
			var lookup = new BurstIdLookup<float3>(16, Allocator.Persistent);

			lookup.Add(1, new float3(1, 2, 3));

			Assert.IsTrue(lookup.ContainsKey(1));
			Assert.IsFalse(lookup.ContainsKey(2));

			lookup.Dispose();
		}

		[Test]
		public void Remove_RemovesItem()
		{
			var lookup = new BurstIdLookup<float3>(16, Allocator.Persistent);

			lookup.Add(1, new float3(1, 2, 3));
			lookup.Add(2, new float3(4, 5, 6));
			lookup.Remove(1);

			Assert.AreEqual(1, lookup.Count);
			Assert.IsFalse(lookup.ContainsKey(1));
			Assert.IsTrue(lookup.ContainsKey(2));

			lookup.Dispose();
		}

		[Test]
		public void Clear_RemovesAllItems()
		{
			var lookup = new BurstIdLookup<float3>(16, Allocator.Persistent);

			lookup.Add(1, new float3(1, 2, 3));
			lookup.Add(2, new float3(4, 5, 6));
			lookup.Clear();

			Assert.AreEqual(0, lookup.Count);

			lookup.Dispose();
		}

		[Test]
		public void Dispose_SetsIsCreatedFalse()
		{
			var lookup = new BurstIdLookup<float3>(16, Allocator.Persistent);

			lookup.Dispose();

			Assert.IsFalse(lookup.IsCreated);
		}
	}
}
