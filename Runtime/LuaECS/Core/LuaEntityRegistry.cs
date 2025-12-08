namespace LuaECS.Core
{
	using LuaECS.Components;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;

	/// <summary>
	/// Unmanaged entity registry state for SharedStatic storage.
	/// Single source of truth for entity ID mappings.
	/// Accessible from Burst-compiled code via SharedStatic.
	/// </summary>
	public struct LuaEntityRegistryData
	{
		public UnsafeHashMap<int, Entity> IdToEntity;
		public UnsafeHashMap<Entity, int> EntityToId;
		public UnsafeHashMap<int, Entity> PendingCreations;
		public UnsafeHashSet<int> PendingDestructions;
		public NativeHashMap<Entity, int> PendingEntityIds;
		public int NextId;
		public bool IsCreated;
	}

	/// <summary>
	/// Static accessor for the entity registry via SharedStatic.
	/// Provides Burst-compatible access to entity ID mappings.
	/// </summary>
	public static class LuaEntityRegistry
	{
		struct RegistryMarker { }

		static readonly SharedStatic<LuaEntityRegistryData> s_Registry =
			SharedStatic<LuaEntityRegistryData>.GetOrCreate<RegistryMarker, LuaEntityRegistryData>();

		public static ref LuaEntityRegistryData Data => ref s_Registry.Data;

		public static bool IsCreated => s_Registry.Data.IsCreated;

		public static int Count => s_Registry.Data.IsCreated ? s_Registry.Data.IdToEntity.Count : 0;

		public static UnsafeHashMap<int, Entity> EntityIdMap =>
			s_Registry.Data.IsCreated ? s_Registry.Data.IdToEntity : default;

		public static void Initialize(int initialCapacity = 256)
		{
			if (s_Registry.Data.IsCreated)
				return;

			s_Registry.Data = new LuaEntityRegistryData
			{
				IdToEntity = new UnsafeHashMap<int, Entity>(initialCapacity, Allocator.Persistent),
				EntityToId = new UnsafeHashMap<Entity, int>(initialCapacity, Allocator.Persistent),
				PendingCreations = new UnsafeHashMap<int, Entity>(64, Allocator.Persistent),
				PendingDestructions = new UnsafeHashSet<int>(64, Allocator.Persistent),
				PendingEntityIds = new NativeHashMap<Entity, int>(64, Allocator.Persistent),
				NextId = 1,
				IsCreated = true,
			};
		}

		public static void Dispose()
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return;

			if (data.IdToEntity.IsCreated)
				data.IdToEntity.Dispose();

			if (data.EntityToId.IsCreated)
				data.EntityToId.Dispose();

			if (data.PendingCreations.IsCreated)
				data.PendingCreations.Dispose();

			if (data.PendingDestructions.IsCreated)
				data.PendingDestructions.Dispose();

			if (data.PendingEntityIds.IsCreated)
				data.PendingEntityIds.Dispose();

			data = default;
		}

		/// <summary>
		/// Clears all registry data without disposing the collections.
		/// Use for test isolation - resets state between tests while keeping allocations.
		/// </summary>
		public static void Clear()
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return;

			if (data.IdToEntity.IsCreated)
				data.IdToEntity.Clear();

			if (data.EntityToId.IsCreated)
				data.EntityToId.Clear();

			if (data.PendingCreations.IsCreated)
				data.PendingCreations.Clear();

			if (data.PendingDestructions.IsCreated)
				data.PendingDestructions.Clear();

			if (data.PendingEntityIds.IsCreated)
				data.PendingEntityIds.Clear();

			data.NextId = 1;
		}

		/// <summary>
		/// O(1) lookup: ID → Entity (Burst-compatible).
		/// Returns Entity.Null if not found.
		/// </summary>
		public static Entity GetEntityFromId(int entityId)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated || entityId <= 0)
				return Entity.Null;

			if (data.PendingCreations.TryGetValue(entityId, out var pending))
				return pending;

			return data.IdToEntity.TryGetValue(entityId, out var entity) ? entity : Entity.Null;
		}

		/// <summary>
		/// O(1) lookup: Entity → ID (Burst-compatible).
		/// Returns -1 if not found.
		/// </summary>
		public static int GetIdFromEntity(Entity entity)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated || entity == Entity.Null)
				return -1;

			return data.EntityToId.TryGetValue(entity, out var id) ? id : -1;
		}

		/// <summary>
		/// O(1) check if ID exists.
		/// </summary>
		public static bool Contains(int entityId)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return false;

			return data.PendingCreations.ContainsKey(entityId) || data.IdToEntity.ContainsKey(entityId);
		}

		/// <summary>
		/// O(1) check if Entity exists.
		/// </summary>
		public static bool Contains(Entity entity)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return false;

			return data.EntityToId.ContainsKey(entity);
		}

		/// <summary>
		/// Returns true if the entity is pending (created this frame).
		/// </summary>
		public static bool IsPending(int entityId)
		{
			ref var data = ref s_Registry.Data;
			return data.IsCreated && data.PendingCreations.ContainsKey(entityId);
		}

		/// <summary>
		/// Returns true if the entity is marked for destruction.
		/// </summary>
		public static bool IsMarkedForDestruction(int entityId)
		{
			ref var data = ref s_Registry.Data;
			return data.IsCreated && data.PendingDestructions.Contains(entityId);
		}

		/// <summary>
		/// Allocates a new entity ID atomically.
		/// </summary>
		public static int AllocateId()
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return -1;

			return data.NextId++;
		}

		/// <summary>
		/// Ensures NextId is at least the given value.
		/// </summary>
		public static void EnsureNextId(int minNextId)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return;

			if (minNextId > data.NextId)
				data.NextId = minNextId;
		}

		/// <summary>
		/// Creates a new entity via ECB. Returns ID immediately.
		/// </summary>
		public static int Create(float3 position, EntityCommandBuffer ecb)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return -1;

			var id = data.NextId++;
			CreateWithId(id, position, ecb);
			return id;
		}

		/// <summary>
		/// Creates an entity with a pre-assigned ID.
		/// </summary>
		public static void CreateWithId(int id, float3 position, EntityCommandBuffer ecb)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return;

			if (id >= data.NextId)
				data.NextId = id + 1;

			var entity = ecb.CreateEntity();
			ecb.AddComponent(entity, LocalTransform.FromPosition(position));
			ecb.AddComponent(entity, new LuaEntityId { Value = id });
			ecb.AddBuffer<LuaScriptRequest>(entity);
			ecb.AddBuffer<LuaEvent>(entity);
			ecb.AddBuffer<LuaCommand>(entity);

			data.PendingCreations[id] = entity;
		}

		/// <summary>
		/// Registers an existing entity. Returns the assigned ID.
		/// </summary>
		public static int Register(Entity entity, EntityCommandBuffer ecb)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return -1;

			if (data.EntityToId.TryGetValue(entity, out var existingId))
				return existingId;

			var id = data.NextId++;
			ecb.AddComponent(entity, new LuaEntityId { Value = id });

			data.IdToEntity[id] = entity;
			data.EntityToId[entity] = id;
			data.PendingCreations[id] = entity;

			return id;
		}

		/// <summary>
		/// Registers a baked entity with sentinel LuaEntityId (Value = 0).
		/// </summary>
		public static int RegisterBaked(Entity entity, EntityCommandBuffer ecb)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return -1;

			if (data.EntityToId.TryGetValue(entity, out var existingId))
				return existingId;

			var id = data.NextId++;
			ecb.SetComponent(entity, new LuaEntityId { Value = id });

			data.IdToEntity[id] = entity;
			data.EntityToId[entity] = id;
			data.PendingCreations[id] = entity;

			return id;
		}

		/// <summary>
		/// Registers an entity immediately (no ECB). Adds or updates LuaEntityId.
		/// </summary>
		public static void RegisterImmediate(Entity entity, int id, EntityManager entityManager)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return;

			if (id >= data.NextId)
				data.NextId = id + 1;

			data.IdToEntity[id] = entity;
			data.EntityToId[entity] = id;

			if (!entityManager.HasComponent<LuaEntityId>(entity))
			{
				entityManager.AddComponentData(entity, new LuaEntityId { Value = id });
			}
			else
			{
				var luaId = entityManager.GetComponentData<LuaEntityId>(entity);
				if (luaId.Value != id)
					entityManager.SetComponentData(entity, new LuaEntityId { Value = id });
			}
		}

		/// <summary>
		/// Marks entity for destruction via ECB.
		/// </summary>
		public static bool Destroy(int entityId, EntityCommandBuffer ecb)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return false;

			if (data.PendingCreations.TryGetValue(entityId, out var pendingEntity))
			{
				ecb.DestroyEntity(pendingEntity);
				data.PendingCreations.Remove(entityId);
				return true;
			}

			if (!data.IdToEntity.TryGetValue(entityId, out var entity))
				return false;

			ecb.RemoveComponent<LuaEntityId>(entity);
			data.PendingDestructions.Add(entityId);
			return true;
		}

		/// <summary>
		/// Commits pending creations after ECB playback.
		/// </summary>
		public static void CommitPendingCreations(EntityManager entityManager)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated || data.PendingCreations.Count == 0)
				return;

			var pendingIds = data.PendingCreations.GetKeyArray(Allocator.Temp);
			var pendingSet = new NativeHashSet<int>(pendingIds.Length, Allocator.Temp);
			foreach (var id in pendingIds)
				pendingSet.Add(id);

			var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LuaEntityId>());
			var entities = query.ToEntityArray(Allocator.Temp);
			var ids = query.ToComponentDataArray<LuaEntityId>(Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				var id = ids[i].Value;
				if (pendingSet.Contains(id) && !data.IdToEntity.ContainsKey(id))
				{
					data.IdToEntity[id] = entities[i];
					data.EntityToId[entities[i]] = id;
				}
			}

			entities.Dispose();
			ids.Dispose();
			pendingIds.Dispose();
			pendingSet.Dispose();
			data.PendingCreations.Clear();
		}

		/// <summary>
		/// Commits pending destructions after ECB playback.
		/// </summary>
		public static void CommitPendingDestructions()
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return;

			foreach (var id in data.PendingDestructions)
			{
				if (data.IdToEntity.TryGetValue(id, out var entity))
				{
					data.EntityToId.Remove(entity);
					data.IdToEntity.Remove(id);
				}
			}

			data.PendingDestructions.Clear();
		}

		/// <summary>
		/// Syncs with world state, removing invalid entities.
		/// </summary>
		public static void SyncWithWorld(EntityManager entityManager)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return;

			var toRemove = new NativeList<int>(Allocator.Temp);

			foreach (var kvp in data.IdToEntity)
			{
				if (!IsEntityValid(entityManager, kvp.Value, kvp.Key))
					toRemove.Add(kvp.Key);
			}

			foreach (var id in toRemove)
			{
				if (data.IdToEntity.TryGetValue(id, out var entity))
				{
					data.EntityToId.Remove(entity);
					data.IdToEntity.Remove(id);
				}
			}

			toRemove.Dispose();
		}

		/// <summary>
		/// Gets all committed entity IDs.
		/// </summary>
		public static NativeArray<int> GetAllIds(Allocator allocator)
		{
			ref var data = ref s_Registry.Data;
			return data.IsCreated
				? data.IdToEntity.GetKeyArray(allocator)
				: new NativeArray<int>(0, allocator);
		}

		/// <summary>
		/// Gets all committed entities.
		/// </summary>
		public static NativeArray<Entity> GetAllEntities(Allocator allocator)
		{
			ref var data = ref s_Registry.Data;
			return data.IsCreated
				? data.IdToEntity.GetValueArray(allocator)
				: new NativeArray<Entity>(0, allocator);
		}

		static bool IsEntityValid(EntityManager entityManager, Entity entity, int expectedId)
		{
			if (entity == Entity.Null)
				return false;

			if (!entityManager.Exists(entity))
				return false;

			if (!entityManager.HasComponent<LuaEntityId>(entity))
				return false;

			var actualId = entityManager.GetComponentData<LuaEntityId>(entity).Value;

			if (actualId == 0)
				return false;

			return actualId == expectedId;
		}

		/// <summary>
		/// Call at start of each frame to commit previous frame's changes
		/// and clear pending state.
		/// </summary>
		public static void BeginFrame(EntityManager entityManager)
		{
			CommitPendingCreations(entityManager);
			CommitPendingDestructions();

			ref var data = ref s_Registry.Data;
			if (data.IsCreated && data.PendingEntityIds.IsCreated)
				data.PendingEntityIds.Clear();
		}

		/// <summary>
		/// Gets or assigns an entity ID. Used during script initialization.
		/// Handles same-frame multiple script initialization on one entity.
		/// Also handles baked entities with sentinel LuaEntityId (Value = 0).
		/// </summary>
		public static int GetOrAssignEntityId(
			Entity entity,
			EntityCommandBuffer ecb,
			EntityManager entityManager
		)
		{
			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return -1;

			var existingId = GetIdFromEntity(entity);
			if (existingId > 0)
				return existingId;

			if (data.PendingEntityIds.TryGetValue(entity, out var pendingId))
				return pendingId;

			if (entityManager.HasComponent<LuaEntityId>(entity))
			{
				var luaId = entityManager.GetComponentData<LuaEntityId>(entity);
				if (luaId.Value == 0)
				{
					var id = RegisterBaked(entity, ecb);
					data.PendingEntityIds[entity] = id;
					return id;
				}
			}

			var newId = Register(entity, ecb);
			data.PendingEntityIds[entity] = newId;
			return newId;
		}

		/// <summary>
		/// O(1) lookup: Entity → ID with pending fallback.
		/// </summary>
		public static int GetEntityIdFromEntity(Entity entity, EntityManager entityManager)
		{
			if (entity == Entity.Null || !entityManager.Exists(entity))
				return -1;

			ref var data = ref s_Registry.Data;
			if (!data.IsCreated)
				return -1;

			var id = GetIdFromEntity(entity);
			if (id > 0)
				return id;

			if (data.PendingEntityIds.TryGetValue(entity, out var pendingId))
				return pendingId;

			return -1;
		}

		/// <summary>
		/// Adds a script request to an entity via ECB.
		/// Works for both existing and pending entities.
		/// </summary>
		public static bool AddScriptDeferred(
			int entityId,
			string scriptName,
			EntityCommandBuffer ecb,
			EntityManager entityManager
		)
		{
			var entity = GetEntityFromId(entityId);
			if (entity == Entity.Null)
			{
				Unity.Logging.Log.Warning(
					"[LuaEntityRegistry] AddScriptDeferred: entity {0} not found",
					entityId
				);
				return false;
			}

			var request = new LuaScriptRequest
			{
				ScriptName = scriptName,
				RequestHash = LuaScriptPathUtility.HashScriptName(scriptName),
				Fulfilled = false,
			};

			if (IsPending(entityId))
			{
				ecb.AppendToBuffer(entity, request);
				return true;
			}

			if (!entityManager.HasBuffer<LuaScriptRequest>(entity))
				ecb.AddBuffer<LuaScriptRequest>(entity);
			if (!entityManager.HasBuffer<LuaEvent>(entity))
				ecb.AddBuffer<LuaEvent>(entity);
			if (!entityManager.HasBuffer<LuaCommand>(entity))
				ecb.AddBuffer<LuaCommand>(entity);

			ecb.AppendToBuffer(entity, request);
			return true;
		}

		/// <summary>
		/// Sets position via ECB.
		/// </summary>
		public static void SetPositionDeferred(int entityId, float3 position, EntityCommandBuffer ecb)
		{
			var entity = GetEntityFromId(entityId);
			if (entity != Entity.Null)
				ecb.SetComponent(entity, LocalTransform.FromPosition(position));
		}

		/// <summary>
		/// Destroys an entity via ECB.
		/// </summary>
		public static void DestroyEntityDeferred(int entityId, EntityCommandBuffer ecb)
		{
			Destroy(entityId, ecb);
		}
	}
}
