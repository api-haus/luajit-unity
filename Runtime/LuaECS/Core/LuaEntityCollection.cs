namespace LuaECS.Core
{
	using LuaECS.Components;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;

	/// <summary>
	/// Owns and guards all Lua-managed entities.
	/// Provides O(1) bidirectional lookup between entity IDs and Entity handles.
	/// All structural changes to Lua entities must go through this collection.
	/// External systems can safely query without cache invalidation concerns.
	/// This is the single source of truth for entity ID mappings.
	/// Uses UnsafeHashMap internally for Burst-compatible access from bridge functions.
	/// </summary>
	public class LuaEntityCollection
	{
		int m_NextId;
		UnsafeHashMap<int, Entity> m_IdToEntity;
		UnsafeHashMap<Entity, int> m_EntityToId;
		UnsafeHashMap<int, Entity> m_PendingCreations;
		UnsafeHashSet<int> m_PendingDestructions;
		EntityManager m_EntityManager;

		public int Count => m_IdToEntity.Count;
		public bool IsCreated => m_IdToEntity.IsCreated;

		/// <summary>
		/// Provides Burst-compatible access to the entity ID map.
		/// Used by bridge functions for O(1) entity lookups.
		/// </summary>
		public UnsafeHashMap<int, Entity> EntityIdMap => m_IdToEntity;

		public LuaEntityCollection(EntityManager entityManager, int initialCapacity = 256)
		{
			m_EntityManager = entityManager;
			m_NextId = 1;
			m_IdToEntity = new UnsafeHashMap<int, Entity>(initialCapacity, Allocator.Persistent);
			m_EntityToId = new UnsafeHashMap<Entity, int>(initialCapacity, Allocator.Persistent);
			m_PendingCreations = new UnsafeHashMap<int, Entity>(64, Allocator.Persistent);
			m_PendingDestructions = new UnsafeHashSet<int>(64, Allocator.Persistent);
		}

		public void Dispose()
		{
			if (m_IdToEntity.IsCreated)
				m_IdToEntity.Dispose();
			if (m_EntityToId.IsCreated)
				m_EntityToId.Dispose();
			if (m_PendingCreations.IsCreated)
				m_PendingCreations.Dispose();
			if (m_PendingDestructions.IsCreated)
				m_PendingDestructions.Dispose();
		}

		/// <summary>
		/// Creates a new Lua entity via ECB. Returns ID immediately.
		/// Entity won't exist until CommitPendingCreations is called after ECB playback.
		/// </summary>
		public int Create(float3 position, EntityCommandBuffer ecb)
		{
			var id = m_NextId++;
			CreateWithId(id, position, ecb);
			return id;
		}

		/// <summary>
		/// Creates an entity with a pre-assigned ID (from Burst bridge's atomic counter).
		/// </summary>
		public void CreateWithId(int id, float3 position, EntityCommandBuffer ecb)
		{
			if (id >= m_NextId)
				m_NextId = id + 1;

			var entity = ecb.CreateEntity();

			ecb.AddComponent(entity, LocalTransform.FromPosition(position));
			ecb.AddComponent(entity, new LuaEntityId { Value = id });
			ecb.AddBuffer<LuaScriptRequest>(entity);
			ecb.AddBuffer<LuaEvent>(entity);
			ecb.AddBuffer<LuaCommand>(entity);

			m_PendingCreations[id] = entity;
		}

		/// <summary>
		/// Registers an existing entity that was created externally (e.g., via authoring).
		/// Returns the assigned ID.
		/// </summary>
		public int Register(Entity entity, EntityCommandBuffer ecb)
		{
			if (m_EntityToId.TryGetValue(entity, out var existingId))
				return existingId;

			var id = m_NextId++;
			ecb.AddComponent(entity, new LuaEntityId { Value = id });

			m_IdToEntity[id] = entity;
			m_EntityToId[entity] = id;

			// Entity won't actually gain LuaEntityId until ECB playback, so treat it as pending
			// to keep Contains/GetEntity valid in the interim.
			m_PendingCreations[id] = entity;
			return id;
		}

		/// <summary>
		/// Registers a baked entity that already has LuaEntityId with sentinel value (0).
		/// Updates the existing component instead of adding a new one.
		/// </summary>
		public int RegisterBaked(Entity entity, EntityCommandBuffer ecb)
		{
			if (m_EntityToId.TryGetValue(entity, out var existingId))
				return existingId;

			var id = m_NextId++;
			ecb.SetComponent(entity, new LuaEntityId { Value = id });

			m_IdToEntity[id] = entity;
			m_EntityToId[entity] = id;
			m_PendingCreations[id] = entity;
			return id;
		}

		/// <summary>
		/// Registers an existing entity immediately (no ECB).
		/// Adds LuaEntityId if missing or updates it if sentinel (0).
		/// </summary>
		public void RegisterImmediate(Entity entity, int id)
		{
			if (id >= m_NextId)
				m_NextId = id + 1;

			m_IdToEntity[id] = entity;
			m_EntityToId[entity] = id;

			// Add or update LuaEntityId
			if (!m_EntityManager.HasComponent<LuaEntityId>(entity))
				m_EntityManager.AddComponentData(entity, new LuaEntityId { Value = id });
			else
			{
				var luaId = m_EntityManager.GetComponentData<LuaEntityId>(entity);
				if (luaId.Value != id)
					m_EntityManager.SetComponentData(entity, new LuaEntityId { Value = id });
			}
		}

		/// <summary>
		/// Marks entity for destruction via ECB.
		/// Removes LuaEntityId to trigger cleanup flow (OnDestroy callbacks, then actual destruction).
		/// Removed from collection after CommitPendingDestructions.
		/// </summary>
		public bool Destroy(int entityId, EntityCommandBuffer ecb)
		{
			if (m_PendingCreations.TryGetValue(entityId, out var pendingEntity))
			{
				ecb.DestroyEntity(pendingEntity);
				m_PendingCreations.Remove(entityId);
				return true;
			}

			if (!m_IdToEntity.TryGetValue(entityId, out var entity))
				return false;

			// Remove LuaEntityId to trigger cleanup flow.
			// LuaScriptCleanupSystem will call OnDestroy, release state, and destroy the entity.
			ecb.RemoveComponent<LuaEntityId>(entity);
			m_PendingDestructions.Add(entityId);
			return true;
		}

		/// <summary>
		/// Call after ECB playback to finalize pending creations.
		/// Resolves deferred Entity handles to real entities.
		/// </summary>
		public void CommitPendingCreations()
		{
			if (m_PendingCreations.Count == 0)
				return;

			var pendingIds = m_PendingCreations.GetKeyArray(Allocator.Temp);
			var pendingSet = new NativeHashSet<int>(pendingIds.Length, Allocator.Temp);
			foreach (var id in pendingIds)
				pendingSet.Add(id);

			var query = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LuaEntityId>());
			var entities = query.ToEntityArray(Allocator.Temp);
			var ids = query.ToComponentDataArray<LuaEntityId>(Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				var id = ids[i].Value;
				if (pendingSet.Contains(id) && !m_IdToEntity.ContainsKey(id))
				{
					m_IdToEntity[id] = entities[i];
					m_EntityToId[entities[i]] = id;
				}
			}

			entities.Dispose();
			ids.Dispose();
			pendingIds.Dispose();
			pendingSet.Dispose();
			m_PendingCreations.Clear();
		}

		/// <summary>
		/// Call after ECB playback to finalize pending destructions.
		/// </summary>
		public void CommitPendingDestructions()
		{
			foreach (var id in m_PendingDestructions)
			{
				if (m_IdToEntity.TryGetValue(id, out var entity))
				{
					m_EntityToId.Remove(entity);
					m_IdToEntity.Remove(id);
				}
			}
			m_PendingDestructions.Clear();
		}

		/// <summary>
		/// O(1) lookup: ID → Entity.
		/// Returns Entity.Null if entity was destroyed (version mismatch).
		/// </summary>
		public Entity GetEntity(int entityId)
		{
			if (entityId <= 0)
				return Entity.Null;

			if (m_PendingCreations.TryGetValue(entityId, out var pending))
				return pending;

			if (!m_IdToEntity.TryGetValue(entityId, out var entity))
				return Entity.Null;

			// Version check: entity may have been destroyed and index recycled
			if (!IsEntityValid(entity, entityId))
			{
				m_EntityToId.Remove(entity);
				m_IdToEntity.Remove(entityId);
				return Entity.Null;
			}

			return entity;
		}

		/// <summary>
		/// O(1) lookup: Entity → ID
		/// </summary>
		public int GetId(Entity entity)
		{
			if (entity == Entity.Null)
				return -1;

			return m_EntityToId.TryGetValue(entity, out var id) ? id : -1;
		}

		/// <summary>
		/// O(1) check if ID exists in collection.
		/// Validates entity version - returns false if entity was destroyed.
		/// </summary>
		public bool Contains(int entityId)
		{
			if (m_PendingCreations.ContainsKey(entityId))
				return true;

			if (!m_IdToEntity.TryGetValue(entityId, out var entity))
				return false;

			if (!IsEntityValid(entity, entityId))
			{
				m_EntityToId.Remove(entity);
				m_IdToEntity.Remove(entityId);
				return false;
			}

			return true;
		}

		/// <summary>
		/// O(1) check if entity exists in collection.
		/// </summary>
		public bool Contains(Entity entity)
		{
			return m_EntityToId.ContainsKey(entity);
		}

		/// <summary>
		/// Returns true if the entity was created this frame and hasn't been committed yet.
		/// </summary>
		public bool IsPending(int entityId)
		{
			return m_PendingCreations.ContainsKey(entityId);
		}

		/// <summary>
		/// Returns true if the entity is marked for destruction this frame.
		/// </summary>
		public bool IsMarkedForDestruction(int entityId)
		{
			return m_PendingDestructions.Contains(entityId);
		}

		/// <summary>
		/// Gets all committed entity IDs. Does not include pending.
		/// </summary>
		public NativeArray<int> GetAllIds(Allocator allocator)
		{
			return m_IdToEntity.GetKeyArray(allocator);
		}

		/// <summary>
		/// Gets all committed entities. Does not include pending.
		/// </summary>
		public NativeArray<Entity> GetAllEntities(Allocator allocator)
		{
			return m_IdToEntity.GetValueArray(allocator);
		}

		/// <summary>
		/// Syncs collection with actual world state.
		/// Call if external systems may have destroyed entities.
		/// </summary>
		public void SyncWithWorld()
		{
			var toRemove = new NativeList<int>(Allocator.Temp);

			foreach (var kvp in m_IdToEntity)
			{
				if (!IsEntityValid(kvp.Value, kvp.Key))
					toRemove.Add(kvp.Key);
			}

			foreach (var id in toRemove)
			{
				if (m_IdToEntity.TryGetValue(id, out var entity))
				{
					m_EntityToId.Remove(entity);
					m_IdToEntity.Remove(id);
				}
			}

			toRemove.Dispose();
		}

		bool IsEntityValid(Entity entity, int expectedId)
		{
			if (entity == Entity.Null)
				return false;

			if (!m_EntityManager.Exists(entity))
				return false;

			if (!m_EntityManager.HasComponent<LuaEntityId>(entity))
				return false;

			var actualId = m_EntityManager.GetComponentData<LuaEntityId>(entity).Value;

			// Sentinel value (0) from baking means entity hasn't been registered yet
			if (actualId == 0)
				return false;

			return actualId == expectedId;
		}
	}
}
