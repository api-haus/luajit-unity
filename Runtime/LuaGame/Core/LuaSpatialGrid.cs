namespace LuaGame.Core
{
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;

	/// <summary>
	/// Entry in the spatial grid for fast queries.
	/// </summary>
	public struct SpatialEntry
	{
		public Entity entity;
		public int entityId;
		public float3 position;
		public float radius;
	}

	/// <summary>
	/// Data for the spatial grid stored in SharedStatic.
	/// </summary>
	public struct LuaSpatialGridData
	{
		public NativeParallelMultiHashMap<int, SpatialEntry> grid;
		public float cellSize;
		public float inverseCellSize;
		public int3 gridMin;
		public int3 gridMax;
		public bool isCreated;
	}

	/// <summary>
	/// Cell-based spatial hashing for efficient damage zone queries.
	/// Uses NativeParallelMultiHashMap for Burst compatibility.
	/// Provides sphere, box, and line sweep queries.
	/// </summary>
	public static class LuaSpatialGrid
	{
		struct SpatialGridMarker { }

		static readonly SharedStatic<LuaSpatialGridData> s_grid =
			SharedStatic<LuaSpatialGridData>.GetOrCreate<SpatialGridMarker, LuaSpatialGridData>();

		public static ref LuaSpatialGridData Data => ref s_grid.Data;
		public static bool IsCreated => s_grid.Data.isCreated;

		/// <summary>
		/// Initializes the spatial grid with given cell size.
		/// Typical cell size: 2-4 units for most games.
		/// </summary>
		public static void Initialize(float cellSize = 4f, int initialCapacity = 1024)
		{
			if (s_grid.Data.isCreated)
				return;

			s_grid.Data = new LuaSpatialGridData
			{
				grid = new NativeParallelMultiHashMap<int, SpatialEntry>(
					initialCapacity,
					Allocator.Persistent
				),
				cellSize = cellSize,
				inverseCellSize = 1f / cellSize,
				gridMin = int3.zero,
				gridMax = int3.zero,
				isCreated = true,
			};
		}

		/// <summary>
		/// Disposes the spatial grid.
		/// </summary>
		public static void Dispose()
		{
			ref var data = ref s_grid.Data;
			if (!data.isCreated)
				return;

			if (data.grid.IsCreated)
				data.grid.Dispose();

			data = default;
		}

		/// <summary>
		/// Clears all entries from the grid.
		/// </summary>
		public static void Clear()
		{
			ref var data = ref s_grid.Data;
			if (data.isCreated && data.grid.IsCreated)
				data.grid.Clear();
		}

		/// <summary>
		/// Converts world position to cell coordinate.
		/// </summary>
		public static int3 PositionToCell(float3 position)
		{
			ref var data = ref s_grid.Data;
			return (int3)math.floor(position * data.inverseCellSize);
		}

		/// <summary>
		/// Converts cell coordinate to hash key.
		/// Uses spatial hashing with prime numbers.
		/// </summary>
		public static int CellToHash(int3 cell)
		{
			unchecked
			{
				return (cell.x * 73856093) ^ (cell.y * 19349663) ^ (cell.z * 83492791);
			}
		}

		/// <summary>
		/// Adds an entry to the grid.
		/// </summary>
		public static void Add(Entity entity, int entityId, float3 position, float radius = 0.5f)
		{
			ref var data = ref s_grid.Data;
			if (!data.isCreated)
				return;

			var entry = new SpatialEntry
			{
				entity = entity,
				entityId = entityId,
				position = position,
				radius = radius,
			};

			// Add to all cells the entity overlaps
			var radiusCells = (int)math.ceil(radius * data.inverseCellSize);
			var centerCell = PositionToCell(position);

			for (var x = -radiusCells; x <= radiusCells; x++)
			{
				for (var y = -radiusCells; y <= radiusCells; y++)
				{
					for (var z = -radiusCells; z <= radiusCells; z++)
					{
						var cell = centerCell + new int3(x, y, z);
						var hash = CellToHash(cell);
						data.grid.Add(hash, entry);
					}
				}
			}
		}

		/// <summary>
		/// Queries all entries within a sphere.
		/// </summary>
		public static void QuerySphere(
			float3 center,
			float radius,
			ref NativeList<SpatialEntry> results
		)
		{
			ref var data = ref s_grid.Data;
			if (!data.isCreated)
				return;

			var radiusSq = radius * radius;
			var radiusCells = (int)math.ceil(radius * data.inverseCellSize);
			var centerCell = PositionToCell(center);

			// Track entities we've already added to avoid duplicates
			var seenEntities = new NativeHashSet<Entity>(64, Allocator.Temp);

			for (var x = -radiusCells; x <= radiusCells; x++)
			{
				for (var y = -radiusCells; y <= radiusCells; y++)
				{
					for (var z = -radiusCells; z <= radiusCells; z++)
					{
						var cell = centerCell + new int3(x, y, z);
						var hash = CellToHash(cell);

						if (data.grid.TryGetFirstValue(hash, out var entry, out var it))
						{
							do
							{
								if (seenEntities.Contains(entry.entity))
									continue;

								var distSq = math.distancesq(center, entry.position);
								var combinedRadius = radius + entry.radius;

								if (distSq <= combinedRadius * combinedRadius)
								{
									results.Add(entry);
									seenEntities.Add(entry.entity);
								}
							} while (data.grid.TryGetNextValue(out entry, ref it));
						}
					}
				}
			}

			seenEntities.Dispose();
		}

		/// <summary>
		/// Queries all entries within an axis-aligned box.
		/// </summary>
		public static void QueryBox(float3 center, float3 extents, ref NativeList<SpatialEntry> results)
		{
			ref var data = ref s_grid.Data;
			if (!data.isCreated)
				return;

			var min = center - extents;
			var max = center + extents;

			var minCell = PositionToCell(min);
			var maxCell = PositionToCell(max);

			var seenEntities = new NativeHashSet<Entity>(64, Allocator.Temp);

			for (var x = minCell.x; x <= maxCell.x; x++)
			{
				for (var y = minCell.y; y <= maxCell.y; y++)
				{
					for (var z = minCell.z; z <= maxCell.z; z++)
					{
						var cell = new int3(x, y, z);
						var hash = CellToHash(cell);

						if (data.grid.TryGetFirstValue(hash, out var entry, out var it))
						{
							do
							{
								if (seenEntities.Contains(entry.entity))
									continue;

								// AABB vs sphere intersection
								var closest = math.clamp(entry.position, min, max);
								var distSq = math.distancesq(closest, entry.position);

								if (distSq <= entry.radius * entry.radius)
								{
									results.Add(entry);
									seenEntities.Add(entry.entity);
								}
							} while (data.grid.TryGetNextValue(out entry, ref it));
						}
					}
				}
			}

			seenEntities.Dispose();
		}

		/// <summary>
		/// Queries all entries along a line segment (for continuous collision detection).
		/// Returns entries sorted by distance from start.
		/// </summary>
		public static void QueryLineSegment(
			float3 start,
			float3 end,
			float radius,
			ref NativeList<SpatialEntry> results
		)
		{
			ref var data = ref s_grid.Data;
			if (!data.isCreated)
				return;

			var direction = end - start;
			var length = math.length(direction);

			if (length < 0.0001f)
			{
				QuerySphere(start, radius, ref results);
				return;
			}

			var normalizedDir = direction / length;

			// Get bounding box of the line segment with radius
			var min = math.min(start, end) - radius;
			var max = math.max(start, end) + radius;

			var minCell = PositionToCell(min);
			var maxCell = PositionToCell(max);

			var seenEntities = new NativeHashSet<Entity>(64, Allocator.Temp);

			for (var x = minCell.x; x <= maxCell.x; x++)
			{
				for (var y = minCell.y; y <= maxCell.y; y++)
				{
					for (var z = minCell.z; z <= maxCell.z; z++)
					{
						var cell = new int3(x, y, z);
						var hash = CellToHash(cell);

						if (data.grid.TryGetFirstValue(hash, out var entry, out var it))
						{
							do
							{
								if (seenEntities.Contains(entry.entity))
									continue;

								// Line segment to sphere intersection
								var toEntry = entry.position - start;
								var projection = math.dot(toEntry, normalizedDir);

								// Clamp to line segment
								projection = math.clamp(projection, 0f, length);
								var closestPoint = start + normalizedDir * projection;

								var distSq = math.distancesq(closestPoint, entry.position);
								var combinedRadius = radius + entry.radius;

								if (distSq <= combinedRadius * combinedRadius)
								{
									results.Add(entry);
									seenEntities.Add(entry.entity);
								}
							} while (data.grid.TryGetNextValue(out entry, ref it));
						}
					}
				}
			}

			seenEntities.Dispose();

			// Sort by distance from start
			SortByDistance(ref results, start);
		}

		/// <summary>
		/// Sorts spatial entries by distance from a point.
		/// </summary>
		static void SortByDistance(ref NativeList<SpatialEntry> entries, float3 origin)
		{
			// Simple insertion sort (usually few entries)
			for (var i = 1; i < entries.Length; i++)
			{
				var key = entries[i];
				var keyDist = math.distancesq(origin, key.position);
				var j = i - 1;

				while (j >= 0 && math.distancesq(origin, entries[j].position) > keyDist)
				{
					entries[j + 1] = entries[j];
					j--;
				}

				entries[j + 1] = key;
			}
		}

		/// <summary>
		/// Queries entries within a capsule (line segment with radius).
		/// Alias for QueryLineSegment for clarity.
		/// </summary>
		public static void QueryCapsule(
			float3 pointA,
			float3 pointB,
			float radius,
			ref NativeList<SpatialEntry> results
		)
		{
			QueryLineSegment(pointA, pointB, radius, ref results);
		}

		/// <summary>
		/// Returns the first hit along a line segment, or null.
		/// Useful for projectiles that hit one target at a time.
		/// </summary>
		public static bool QueryLineSegmentFirst(
			float3 start,
			float3 end,
			float radius,
			out SpatialEntry hit
		)
		{
			hit = default;

			var results = new NativeList<SpatialEntry>(16, Allocator.Temp);
			QueryLineSegment(start, end, radius, ref results);

			var found = results.Length > 0;
			if (found)
				hit = results[0];

			results.Dispose();
			return found;
		}
	}
}
