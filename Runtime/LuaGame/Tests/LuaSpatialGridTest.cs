namespace LuaGame.Tests
{
	using LuaGame.Core;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;

	[TestFixture]
	public class LuaSpatialGridTest
	{
		[SetUp]
		public void SetUp()
		{
			if (LuaSpatialGrid.IsCreated)
				LuaSpatialGrid.Dispose();

			LuaSpatialGrid.Initialize(cellSize: 4f, initialCapacity: 256);
		}

		[TearDown]
		public void TearDown()
		{
			if (LuaSpatialGrid.IsCreated)
				LuaSpatialGrid.Dispose();
		}

		[Test]
		public void Initialize_CreatesGrid()
		{
			Assert.IsTrue(LuaSpatialGrid.IsCreated);
		}

		[Test]
		public void Initialize_DoesNotReinitialize()
		{
			var data1 = LuaSpatialGrid.Data;
			LuaSpatialGrid.Initialize(cellSize: 8f, initialCapacity: 512);
			var data2 = LuaSpatialGrid.Data;

			Assert.AreEqual(data1.cellSize, data2.cellSize, "Should not reinitialize");
		}

		[Test]
		public void Add_AddsEntryToGrid()
		{
			var entity = new Entity { Index = 1, Version = 1 };
			LuaSpatialGrid.Add(entity, 1, new float3(0, 0, 0), 0.5f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QuerySphere(float3.zero, 1f, ref results);

			Assert.AreEqual(1, results.Length);
			Assert.AreEqual(1, results[0].entityId);

			results.Dispose();
		}

		[Test]
		public void Add_StoresCorrectData()
		{
			var entity = new Entity { Index = 42, Version = 3 };
			var position = new float3(5, 10, 15);
			var radius = 2.5f;

			LuaSpatialGrid.Add(entity, 100, position, radius);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QuerySphere(position, 0.1f, ref results);

			Assert.AreEqual(1, results.Length);
			Assert.AreEqual(100, results[0].entityId);
			Assert.AreEqual(position, results[0].position);
			Assert.AreEqual(radius, results[0].radius);
			Assert.AreEqual(entity, results[0].entity);

			results.Dispose();
		}

		[Test]
		public void QuerySphere_FindsEntitiesInRadius()
		{
			var entity1 = new Entity { Index = 1, Version = 1 };
			var entity2 = new Entity { Index = 2, Version = 1 };
			var entity3 = new Entity { Index = 3, Version = 1 };

			LuaSpatialGrid.Add(entity1, 1, new float3(0, 0, 0), 0.5f);
			LuaSpatialGrid.Add(entity2, 2, new float3(2, 0, 0), 0.5f);
			LuaSpatialGrid.Add(entity3, 3, new float3(10, 0, 0), 0.5f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QuerySphere(float3.zero, 3f, ref results);

			Assert.AreEqual(2, results.Length, "Should find 2 entities within radius 3");

			results.Dispose();
		}

		[Test]
		public void QuerySphere_ExcludesEntitiesOutsideRadius()
		{
			var entity = new Entity { Index = 1, Version = 1 };
			LuaSpatialGrid.Add(entity, 1, new float3(100, 0, 0), 0.5f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QuerySphere(float3.zero, 5f, ref results);

			Assert.AreEqual(0, results.Length, "Should not find entity outside radius");

			results.Dispose();
		}

		[Test]
		public void QuerySphere_ConsidersEntityRadius()
		{
			var entity = new Entity { Index = 1, Version = 1 };
			// Entity at distance 5 with radius 2 should intersect query sphere of radius 4
			LuaSpatialGrid.Add(entity, 1, new float3(5, 0, 0), 2f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QuerySphere(float3.zero, 4f, ref results);

			Assert.AreEqual(1, results.Length, "Should find entity due to combined radii");

			results.Dispose();
		}

		[Test]
		public void QuerySphere_EmptyGrid()
		{
			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QuerySphere(float3.zero, 100f, ref results);

			Assert.AreEqual(0, results.Length);

			results.Dispose();
		}

		[Test]
		public void QueryBox_FindsEntitiesInBox()
		{
			var entity1 = new Entity { Index = 1, Version = 1 };
			var entity2 = new Entity { Index = 2, Version = 1 };

			LuaSpatialGrid.Add(entity1, 1, new float3(1, 1, 1), 0.5f);
			LuaSpatialGrid.Add(entity2, 2, new float3(10, 10, 10), 0.5f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QueryBox(float3.zero, new float3(3, 3, 3), ref results);

			Assert.AreEqual(1, results.Length, "Should find 1 entity in box");
			Assert.AreEqual(1, results[0].entityId);

			results.Dispose();
		}

		[Test]
		public void QueryBox_ExcludesEntitiesOutside()
		{
			var entity = new Entity { Index = 1, Version = 1 };
			LuaSpatialGrid.Add(entity, 1, new float3(10, 10, 10), 0.5f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QueryBox(float3.zero, new float3(2, 2, 2), ref results);

			Assert.AreEqual(0, results.Length, "Should not find entity outside box");

			results.Dispose();
		}

		[Test]
		public void QueryBox_NonSymmetricExtents()
		{
			var entity1 = new Entity { Index = 1, Version = 1 };
			var entity2 = new Entity { Index = 2, Version = 1 };

			LuaSpatialGrid.Add(entity1, 1, new float3(5, 0, 0), 0.5f); // In tall box
			LuaSpatialGrid.Add(entity2, 2, new float3(0, 5, 0), 0.5f); // Outside

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QueryBox(float3.zero, new float3(10, 1, 1), ref results);

			Assert.AreEqual(1, results.Length);
			Assert.AreEqual(1, results[0].entityId);

			results.Dispose();
		}

		[Test]
		public void QueryLineSegment_FindsEntitiesAlongLine()
		{
			var entity1 = new Entity { Index = 1, Version = 1 };
			var entity2 = new Entity { Index = 2, Version = 1 };
			var entity3 = new Entity { Index = 3, Version = 1 };

			LuaSpatialGrid.Add(entity1, 1, new float3(5, 0, 0), 0.5f);
			LuaSpatialGrid.Add(entity2, 2, new float3(0, 5, 0), 0.5f); // Off the line
			LuaSpatialGrid.Add(entity3, 3, new float3(10, 0, 0), 0.5f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QueryLineSegment(new float3(0, 0, 0), new float3(15, 0, 0), 0.5f, ref results);

			Assert.AreEqual(2, results.Length, "Should find 2 entities along line");

			results.Dispose();
		}

		[Test]
		public void QueryLineSegment_SortsByDistance()
		{
			var entity1 = new Entity { Index = 1, Version = 1 };
			var entity2 = new Entity { Index = 2, Version = 1 };

			LuaSpatialGrid.Add(entity1, 1, new float3(10, 0, 0), 0.5f);
			LuaSpatialGrid.Add(entity2, 2, new float3(5, 0, 0), 0.5f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QueryLineSegment(new float3(0, 0, 0), new float3(15, 0, 0), 0.5f, ref results);

			Assert.AreEqual(2, results.Length);
			Assert.AreEqual(2, results[0].entityId, "Closer entity should be first");
			Assert.AreEqual(1, results[1].entityId);

			results.Dispose();
		}

		[Test]
		public void QueryLineSegment_ZeroLength_FallsBackToSphere()
		{
			var entity = new Entity { Index = 1, Version = 1 };
			LuaSpatialGrid.Add(entity, 1, new float3(1, 0, 0), 0.5f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QueryLineSegment(float3.zero, float3.zero, 2f, ref results);

			Assert.AreEqual(1, results.Length);

			results.Dispose();
		}

		[Test]
		public void QueryLineSegment_DiagonalLine()
		{
			var entity1 = new Entity { Index = 1, Version = 1 };
			var entity2 = new Entity { Index = 2, Version = 1 };

			LuaSpatialGrid.Add(entity1, 1, new float3(5, 5, 5), 1f);
			LuaSpatialGrid.Add(entity2, 2, new float3(0, 10, 0), 1f); // Off diagonal

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QueryLineSegment(float3.zero, new float3(10, 10, 10), 1f, ref results);

			Assert.AreEqual(1, results.Length);
			Assert.AreEqual(1, results[0].entityId);

			results.Dispose();
		}

		[Test]
		public void QueryCapsule_SameAsLineSegment()
		{
			var entity = new Entity { Index = 1, Version = 1 };
			LuaSpatialGrid.Add(entity, 1, new float3(5, 0, 0), 0.5f);

			var lineResults = new NativeList<SpatialEntry>(16, Allocator.Temp);
			var capsuleResults = new NativeList<SpatialEntry>(16, Allocator.Temp);

			LuaSpatialGrid.QueryLineSegment(float3.zero, new float3(10, 0, 0), 1f, ref lineResults);
			LuaSpatialGrid.QueryCapsule(float3.zero, new float3(10, 0, 0), 1f, ref capsuleResults);

			Assert.AreEqual(lineResults.Length, capsuleResults.Length);

			lineResults.Dispose();
			capsuleResults.Dispose();
		}

		[Test]
		public void QueryLineSegmentFirst_ReturnsClosest()
		{
			var entity1 = new Entity { Index = 1, Version = 1 };
			var entity2 = new Entity { Index = 2, Version = 1 };

			LuaSpatialGrid.Add(entity1, 1, new float3(10, 0, 0), 0.5f);
			LuaSpatialGrid.Add(entity2, 2, new float3(5, 0, 0), 0.5f);

			var found = LuaSpatialGrid.QueryLineSegmentFirst(
				float3.zero,
				new float3(15, 0, 0),
				0.5f,
				out var hit
			);

			Assert.IsTrue(found);
			Assert.AreEqual(2, hit.entityId, "Should return closest entity");
		}

		[Test]
		public void QueryLineSegmentFirst_ReturnsFalseWhenEmpty()
		{
			var found = LuaSpatialGrid.QueryLineSegmentFirst(
				float3.zero,
				new float3(15, 0, 0),
				0.5f,
				out _
			);

			Assert.IsFalse(found);
		}

		[Test]
		public void Clear_RemovesAllEntries()
		{
			var entity = new Entity { Index = 1, Version = 1 };
			LuaSpatialGrid.Add(entity, 1, float3.zero, 0.5f);

			LuaSpatialGrid.Clear();

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QuerySphere(float3.zero, 100f, ref results);

			Assert.AreEqual(0, results.Length, "Grid should be empty after clear");

			results.Dispose();
		}

		[Test]
		public void PositionToCell_ConvertsCorrectly()
		{
			var cell = LuaSpatialGrid.PositionToCell(new float3(5, 10, -3));

			// With cell size 4: 5/4=1, 10/4=2, -3/4=-1
			Assert.AreEqual(1, cell.x);
			Assert.AreEqual(2, cell.y);
			Assert.AreEqual(-1, cell.z);
		}

		[Test]
		public void PositionToCell_NegativeCoordinates()
		{
			var cell = LuaSpatialGrid.PositionToCell(new float3(-5, -10, -15));

			Assert.AreEqual(-2, cell.x);
			Assert.AreEqual(-3, cell.y);
			Assert.AreEqual(-4, cell.z);
		}

		[Test]
		public void PositionToCell_Origin()
		{
			var cell = LuaSpatialGrid.PositionToCell(float3.zero);

			Assert.AreEqual(int3.zero, cell);
		}

		[Test]
		public void CellToHash_DeterministicHashing()
		{
			var cell = new int3(1, 2, 3);

			var hash1 = LuaSpatialGrid.CellToHash(cell);
			var hash2 = LuaSpatialGrid.CellToHash(cell);

			Assert.AreEqual(hash1, hash2);
		}

		[Test]
		public void CellToHash_DifferentCellsDifferentHashes()
		{
			var hash1 = LuaSpatialGrid.CellToHash(new int3(1, 2, 3));
			var hash2 = LuaSpatialGrid.CellToHash(new int3(3, 2, 1));

			Assert.AreNotEqual(hash1, hash2);
		}

		[Test]
		public void QuerySphere_NoDuplicates()
		{
			// Entity with large radius spans multiple cells
			var entity = new Entity { Index = 1, Version = 1 };
			LuaSpatialGrid.Add(entity, 1, float3.zero, 5f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QuerySphere(float3.zero, 10f, ref results);

			Assert.AreEqual(1, results.Length, "Should not have duplicates");

			results.Dispose();
		}

		[Test]
		public void QueryBox_NoDuplicates()
		{
			var entity = new Entity { Index = 1, Version = 1 };
			LuaSpatialGrid.Add(entity, 1, float3.zero, 5f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QueryBox(float3.zero, new float3(10, 10, 10), ref results);

			Assert.AreEqual(1, results.Length, "Should not have duplicates");

			results.Dispose();
		}

		[Test]
		public void ManyEntities_Performance()
		{
			// Add 100 entities
			for (var i = 0; i < 100; i++)
			{
				var entity = new Entity { Index = i, Version = 1 };
				var pos = new float3(i * 2, 0, 0);
				LuaSpatialGrid.Add(entity, i, pos, 0.5f);
			}

			var results = new NativeList<SpatialEntry>(100, Allocator.Temp);
			LuaSpatialGrid.QuerySphere(new float3(50, 0, 0), 20f, ref results);

			Assert.Greater(results.Length, 0);
			Assert.Less(results.Length, 100);

			results.Dispose();
		}

		[Test]
		public void NegativePositions_WorkCorrectly()
		{
			var entity = new Entity { Index = 1, Version = 1 };
			LuaSpatialGrid.Add(entity, 1, new float3(-50, -50, -50), 1f);

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			LuaSpatialGrid.QuerySphere(new float3(-50, -50, -50), 5f, ref results);

			Assert.AreEqual(1, results.Length);

			results.Dispose();
		}
	}
}
