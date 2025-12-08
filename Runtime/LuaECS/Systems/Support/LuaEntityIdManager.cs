namespace LuaECS.Systems.Support
{
	using LuaECS.Components;
	using LuaECS.Core;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics;
	using Unity.Transforms;

	/// <summary>
	/// Facade for entity operations that delegates to LuaEntityCollection.
	/// Handles script and component operations on Lua-managed entities.
	/// </summary>
	public class LuaEntityIdManager
	{
		readonly LuaEntityCollection m_Collection;
		readonly EntityManager m_EntityManager;
		NativeHashMap<Entity, int> m_PendingEntityIds;

		public LuaEntityCollection Collection => m_Collection;

		public LuaEntityIdManager(EntityManager entityManager)
		{
			m_EntityManager = entityManager;
			m_Collection = new LuaEntityCollection(entityManager);
			m_PendingEntityIds = new NativeHashMap<Entity, int>(64, Allocator.Persistent);
		}

		public void Dispose()
		{
			m_Collection?.Dispose();
			if (m_PendingEntityIds.IsCreated)
				m_PendingEntityIds.Dispose();
		}

		/// <summary>
		/// Call at start of each frame to commit previous frame's changes
		/// and clear pending state.
		/// </summary>
		public void BeginFrame()
		{
			m_Collection.CommitPendingCreations();
			m_Collection.CommitPendingDestructions();
			m_PendingEntityIds.Clear();
		}

		/// <summary>
		/// Gets or assigns an entity ID. Used during script initialization.
		/// Handles same-frame multiple script initialization on one entity.
		/// Also handles baked entities with sentinel LuaEntityId (Value = 0).
		/// </summary>
		public int GetOrAssignEntityId(Entity entity, EntityCommandBuffer ecb)
		{
			var existingId = m_Collection.GetId(entity);
			if (existingId > 0)
				return existingId;

			if (m_PendingEntityIds.TryGetValue(entity, out var pendingId))
				return pendingId;

			// Check if entity has LuaEntityId with sentinel value (0) from baking
			if (m_EntityManager.HasComponent<LuaEntityId>(entity))
			{
				var luaId = m_EntityManager.GetComponentData<LuaEntityId>(entity);
				if (luaId.Value == 0)
				{
					// Baked entity with sentinel - assign real ID and register
					var id = m_Collection.RegisterBaked(entity, ecb);
					m_PendingEntityIds[entity] = id;
					return id;
				}
			}

			var newId = m_Collection.Register(entity, ecb);
			m_PendingEntityIds[entity] = newId;
			return newId;
		}

		/// <summary>
		/// Creates a new entity via ECB. Returns ID immediately.
		/// </summary>
		public int CreateEntityDeferred(float3 position, EntityCommandBuffer ecb)
		{
			return m_Collection.Create(position, ecb);
		}

		/// <summary>
		/// Creates an entity with a pre-assigned ID (from Burst bridge).
		/// </summary>
		public void CreateEntityWithId(int entityId, float3 position, EntityCommandBuffer ecb)
		{
			m_Collection.CreateWithId(entityId, position, ecb);
		}

		/// <summary>
		/// Adds a script request to an entity via ECB.
		/// Works for both existing and pending entities.
		/// </summary>
		public bool AddScriptDeferred(int entityId, string scriptName, EntityCommandBuffer ecb)
		{
			var entity = m_Collection.GetEntity(entityId);
			if (entity == Entity.Null)
			{
				Log.Warning("[LuaEntityIdManager] AddScriptDeferred: entity {0} not found", entityId);
				return false;
			}

			var request = new LuaScriptRequest
			{
				ScriptName = scriptName,
				RequestHash = LuaScriptPathUtility.HashScriptName(scriptName),
				Fulfilled = false,
			};

			if (m_Collection.IsPending(entityId))
			{
				ecb.AppendToBuffer(entity, request);
				return true;
			}

			if (!m_EntityManager.HasBuffer<LuaScriptRequest>(entity))
				ecb.AddBuffer<LuaScriptRequest>(entity);
			if (!m_EntityManager.HasBuffer<LuaEvent>(entity))
				ecb.AddBuffer<LuaEvent>(entity);
			if (!m_EntityManager.HasBuffer<LuaCommand>(entity))
				ecb.AddBuffer<LuaCommand>(entity);

			ecb.AppendToBuffer(entity, request);

			return true;
		}

		/// <summary>
		/// Sets position via ECB.
		/// </summary>
		public void SetPositionDeferred(int entityId, float3 position, EntityCommandBuffer ecb)
		{
			var entity = m_Collection.GetEntity(entityId);
			if (entity != Entity.Null)
				ecb.SetComponent(entity, LocalTransform.FromPosition(position));
		}

		/// <summary>
		/// Destroys an entity via ECB.
		/// </summary>
		public void DestroyEntityDeferred(int entityId, EntityCommandBuffer ecb)
		{
			m_Collection.Destroy(entityId, ecb);
		}

		/// <summary>
		/// O(1) lookup: Entity → ID
		/// </summary>
		public int GetEntityIdFromEntity(Entity entity)
		{
			if (entity == Entity.Null || !m_EntityManager.Exists(entity))
				return -1;

			var id = m_Collection.GetId(entity);
			if (id > 0)
				return id;

			if (m_PendingEntityIds.TryGetValue(entity, out var pendingId))
				return pendingId;

			return -1;
		}

		/// <summary>
		/// O(1) lookup: ID → Entity
		/// </summary>
		public Entity GetEntityFromId(int entityId)
		{
			return m_Collection.GetEntity(entityId);
		}

		/// <summary>
		/// Returns true if entity was created this frame (pending ECB playback).
		/// </summary>
		public bool IsDeferred(int entityId)
		{
			return m_Collection.IsPending(entityId);
		}

		/// <summary>
		/// Registers an externally-created entity that already has LuaEntityId.
		/// </summary>
		public void RegisterExisting(Entity entity, int id)
		{
			m_Collection.RegisterImmediate(entity, id);
		}

		/// <summary>
		/// Syncs collection with actual world state.
		/// Call periodically or after external entity destruction.
		/// </summary>
		public void SyncWithWorld()
		{
			m_Collection.SyncWithWorld();
		}
	}
}
