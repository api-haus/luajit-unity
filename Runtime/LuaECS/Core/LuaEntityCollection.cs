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
	///
	/// Note: This class now delegates to LuaEntityRegistry SharedStatic for backward compatibility.
	/// Will be removed in a future refactoring step.
	/// </summary>
	public class LuaEntityCollection
	{
		readonly EntityManager m_EntityManager;
		bool m_IsDisposed;

		public int Count => m_IsDisposed ? 0 : LuaEntityRegistry.Count;
		public bool IsCreated => !m_IsDisposed && LuaEntityRegistry.IsCreated;

		/// <summary>
		/// Provides Burst-compatible access to the entity ID map.
		/// Used by bridge functions for O(1) entity lookups.
		/// </summary>
		public UnsafeHashMap<int, Entity> EntityIdMap => LuaEntityRegistry.EntityIdMap;

		public LuaEntityCollection(EntityManager entityManager, int initialCapacity = 256)
		{
			m_EntityManager = entityManager;
			// Note: LuaEntityRegistry is managed by LuaScriptFulfillmentSystem.
			// This class is a backward-compatible wrapper that doesn't own the lifecycle.
			LuaEntityRegistry.Initialize(initialCapacity);
		}

		public void Dispose()
		{
			if (m_IsDisposed)
				return;

			m_IsDisposed = true;

			// Note: Don't dispose LuaEntityRegistry here - it's managed by LuaScriptFulfillmentSystem.
			// Multiple LuaEntityCollection instances may exist (e.g., in tests) but they share
			// the same static registry. Only the system should dispose it.
			// However, we do clear the data for test isolation.
			LuaEntityRegistry.Clear();
		}

		/// <summary>
		/// Creates a new Lua entity via ECB. Returns ID immediately.
		/// Entity won't exist until CommitPendingCreations is called after ECB playback.
		/// </summary>
		public int Create(float3 position, EntityCommandBuffer ecb)
		{
			return LuaEntityRegistry.Create(position, ecb);
		}

		/// <summary>
		/// Creates an entity with a pre-assigned ID (from Burst bridge's atomic counter).
		/// </summary>
		public void CreateWithId(int id, float3 position, EntityCommandBuffer ecb)
		{
			LuaEntityRegistry.CreateWithId(id, position, ecb);
		}

		/// <summary>
		/// Registers an existing entity that was created externally (e.g., via authoring).
		/// Returns the assigned ID.
		/// </summary>
		public int Register(Entity entity, EntityCommandBuffer ecb)
		{
			return LuaEntityRegistry.Register(entity, ecb);
		}

		/// <summary>
		/// Registers a baked entity that already has LuaEntityId with sentinel value (0).
		/// Updates the existing component instead of adding a new one.
		/// </summary>
		public int RegisterBaked(Entity entity, EntityCommandBuffer ecb)
		{
			return LuaEntityRegistry.RegisterBaked(entity, ecb);
		}

		/// <summary>
		/// Registers an existing entity immediately (no ECB).
		/// Adds LuaEntityId if missing or updates it if sentinel (0).
		/// </summary>
		public void RegisterImmediate(Entity entity, int id)
		{
			LuaEntityRegistry.RegisterImmediate(entity, id, m_EntityManager);
		}

		/// <summary>
		/// Marks entity for destruction via ECB.
		/// Removes LuaEntityId to trigger cleanup flow (OnDestroy callbacks, then actual destruction).
		/// Removed from collection after CommitPendingDestructions.
		/// </summary>
		public bool Destroy(int entityId, EntityCommandBuffer ecb)
		{
			return LuaEntityRegistry.Destroy(entityId, ecb);
		}

		/// <summary>
		/// Call after ECB playback to finalize pending creations.
		/// Resolves deferred Entity handles to real entities.
		/// </summary>
		public void CommitPendingCreations()
		{
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);
		}

		/// <summary>
		/// Call after ECB playback to finalize pending destructions.
		/// </summary>
		public void CommitPendingDestructions()
		{
			LuaEntityRegistry.CommitPendingDestructions();
		}

		/// <summary>
		/// O(1) lookup: ID → Entity.
		/// Returns Entity.Null if entity was destroyed (version mismatch).
		/// </summary>
		public Entity GetEntity(int entityId)
		{
			if (entityId <= 0)
				return Entity.Null;

			// Pending entities are valid - skip version check
			if (LuaEntityRegistry.IsPending(entityId))
			{
				ref var data = ref LuaEntityRegistry.Data;
				return data.PendingCreations.TryGetValue(entityId, out var pending) ? pending : Entity.Null;
			}

			var entity = LuaEntityRegistry.GetEntityFromId(entityId);
			if (entity == Entity.Null)
				return Entity.Null;

			if (!IsEntityValid(entity, entityId))
			{
				ref var data = ref LuaEntityRegistry.Data;
				data.EntityToId.Remove(entity);
				data.IdToEntity.Remove(entityId);
				return Entity.Null;
			}

			return entity;
		}

		/// <summary>
		/// O(1) lookup: Entity → ID
		/// </summary>
		public int GetId(Entity entity)
		{
			return LuaEntityRegistry.GetIdFromEntity(entity);
		}

		/// <summary>
		/// O(1) check if ID exists in collection.
		/// Validates entity version - returns false if entity was destroyed.
		/// </summary>
		public bool Contains(int entityId)
		{
			if (LuaEntityRegistry.IsPending(entityId))
				return true;

			var entity = LuaEntityRegistry.GetEntityFromId(entityId);
			if (entity == Entity.Null)
				return false;

			if (!IsEntityValid(entity, entityId))
			{
				ref var data = ref LuaEntityRegistry.Data;
				data.EntityToId.Remove(entity);
				data.IdToEntity.Remove(entityId);
				return false;
			}

			return true;
		}

		/// <summary>
		/// O(1) check if entity exists in collection.
		/// </summary>
		public bool Contains(Entity entity)
		{
			return LuaEntityRegistry.Contains(entity);
		}

		/// <summary>
		/// Returns true if the entity was created this frame and hasn't been committed yet.
		/// </summary>
		public bool IsPending(int entityId)
		{
			return LuaEntityRegistry.IsPending(entityId);
		}

		/// <summary>
		/// Returns true if the entity is marked for destruction this frame.
		/// </summary>
		public bool IsMarkedForDestruction(int entityId)
		{
			return LuaEntityRegistry.IsMarkedForDestruction(entityId);
		}

		/// <summary>
		/// Gets all committed entity IDs. Does not include pending.
		/// </summary>
		public NativeArray<int> GetAllIds(Allocator allocator)
		{
			return LuaEntityRegistry.GetAllIds(allocator);
		}

		/// <summary>
		/// Gets all committed entities. Does not include pending.
		/// </summary>
		public NativeArray<Entity> GetAllEntities(Allocator allocator)
		{
			return LuaEntityRegistry.GetAllEntities(allocator);
		}

		/// <summary>
		/// Syncs collection with actual world state.
		/// Call if external systems may have destroyed entities.
		/// </summary>
		public void SyncWithWorld()
		{
			LuaEntityRegistry.SyncWithWorld(m_EntityManager);
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

			if (actualId == 0)
				return false;

			return actualId == expectedId;
		}
	}
}
