namespace LuaVM.Burst
{
	using System.Threading;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;

	/// <summary>
	/// Burst-compatible ID allocator using atomic operations.
	/// Thread-safe for use across multiple jobs.
	/// </summary>
	[BurstCompile]
	public struct BurstIdAllocator
	{
		struct IdMarker { }

		static readonly SharedStatic<int> s_nextId = SharedStatic<int>.GetOrCreate<IdMarker>();

		/// <summary>
		/// Atomically allocate a new unique ID.
		/// </summary>
		public static int Allocate()
		{
			return Interlocked.Increment(ref s_nextId.Data);
		}

		/// <summary>
		/// Get the current next ID value without allocating.
		/// </summary>
		public static int Current => s_nextId.Data;

		/// <summary>
		/// Sync the allocator to ensure IDs start from at least the given value.
		/// </summary>
		public static void SyncMinimum(int minId)
		{
			var current = s_nextId.Data;
			while (current < minId)
			{
				var prev = Interlocked.CompareExchange(ref s_nextId.Data, minId, current);
				if (prev == current)
					break;
				current = prev;
			}
		}

		/// <summary>
		/// Reset the allocator to start from 1.
		/// </summary>
		public static void Reset()
		{
			s_nextId.Data = 0;
		}
	}

	/// <summary>
	/// Burst-compatible pending operation for deferred execution.
	/// Generic container for operations that need to be queued during Burst jobs.
	/// </summary>
	/// <typeparam name="T">The operation data type (must be unmanaged)</typeparam>
	[BurstCompile]
	public unsafe struct BurstOperationQueue<T>
		where T : unmanaged
	{
		[NativeDisableUnsafePtrRestriction]
		UnsafeList<T>* m_Operations;

		public bool IsCreated => m_Operations != null;
		public int Length => m_Operations != null ? m_Operations->Length : 0;

		public BurstOperationQueue(int initialCapacity, Allocator allocator)
		{
			m_Operations = UnsafeList<T>.Create(initialCapacity, allocator);
		}

		public void Add(T operation)
		{
			if (m_Operations != null)
				m_Operations->Add(operation);
		}

		public T this[int index] => (*m_Operations)[index];

		public void Clear()
		{
			if (m_Operations != null)
				m_Operations->Clear();
		}

		public void Dispose()
		{
			if (m_Operations != null)
			{
				UnsafeList<T>.Destroy(m_Operations);
				m_Operations = null;
			}
		}

		/// <summary>
		/// Get the underlying pointer for use in Burst contexts.
		/// </summary>
		public UnsafeList<T>* GetUnsafePtr() => m_Operations;
	}

	/// <summary>
	/// Burst-compatible lookup table for ID to value mapping.
	/// Uses UnsafeHashMap internally for thread-safe reads.
	/// </summary>
	/// <typeparam name="TValue">The value type (must be unmanaged)</typeparam>
	[BurstCompile]
	public struct BurstIdLookup<TValue>
		where TValue : unmanaged
	{
		UnsafeHashMap<int, TValue> m_Map;

		public bool IsCreated => m_Map.IsCreated;
		public int Count => m_Map.Count;

		public BurstIdLookup(int initialCapacity, Allocator allocator)
		{
			m_Map = new UnsafeHashMap<int, TValue>(initialCapacity, allocator);
		}

		public bool TryGetValue(int id, out TValue value)
		{
			return m_Map.TryGetValue(id, out value);
		}

		public bool ContainsKey(int id)
		{
			return m_Map.ContainsKey(id);
		}

		public void Add(int id, TValue value)
		{
			m_Map.TryAdd(id, value);
		}

		public void Remove(int id)
		{
			m_Map.Remove(id);
		}

		public void Clear()
		{
			m_Map.Clear();
		}

		public void Dispose()
		{
			if (m_Map.IsCreated)
				m_Map.Dispose();
		}

		/// <summary>
		/// Get the underlying UnsafeHashMap for direct access in Burst contexts.
		/// </summary>
		public UnsafeHashMap<int, TValue> GetUnsafeMap() => m_Map;
	}
}
