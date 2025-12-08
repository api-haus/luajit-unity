namespace LuaECS.Core
{
	using Components;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics;
	using Unity.Transforms;

	/// <summary>
	/// Unmanaged entity registry state for SharedStatic storage.
	/// Single source of truth for entity ID mappings.
	/// Accessible from Burst-compiled code via SharedStatic.
	/// </summary>
	public struct LuaEntityRegistryData
	{
		public UnsafeHashMap<int, Entity> idToEntity;
		public UnsafeHashMap<Entity, int> entityToId;
		public UnsafeHashMap<int, Entity> pendingCreations;
		public UnsafeHashSet<int> pendingDestructions;
		public NativeHashMap<Entity, int> pendingEntityIds;
		public int nextId;
		public bool isCreated;
	}

	/// <summary>
	/// Static accessor for the entity registry via SharedStatic.
	/// Provides Burst-compatible access to entity ID mappings.
	/// </summary>
	public static class LuaEntityRegistry
	{
		struct RegistryMarker { }

		static readonly SharedStatic<LuaEntityRegistryData> s_registry =
			SharedStatic<LuaEntityRegistryData>.GetOrCreate<RegistryMarker, LuaEntityRegistryData>();

		public static ref LuaEntityRegistryData Data => ref s_registry.Data;

		public static bool IsCreated => s_registry.Data.isCreated;

		public static int Count => s_registry.Data.isCreated ? s_registry.Data.idToEntity.Count : 0;

		public static UnsafeHashMap<int, Entity> EntityIdMap =>
			s_registry.Data.isCreated ? s_registry.Data.idToEntity : default;

		public static void Initialize(int initialCapacity = 256)
		{
			if (s_registry.Data.isCreated)
				return;

			s_registry.Data = new LuaEntityRegistryData
			{
				idToEntity = new UnsafeHashMap<int, Entity>(initialCapacity, Allocator.Persistent),
				entityToId = new UnsafeHashMap<Entity, int>(initialCapacity, Allocator.Persistent),
				pendingCreations = new UnsafeHashMap<int, Entity>(64, Allocator.Persistent),
				pendingDestructions = new UnsafeHashSet<int>(64, Allocator.Persistent),
				pendingEntityIds = new NativeHashMap<Entity, int>(64, Allocator.Persistent),
				nextId = 1,
				isCreated = true,
			};
		}

		public static void Dispose()
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return;

			if (data.idToEntity.IsCreated)
				data.idToEntity.Dispose();

			if (data.entityToId.IsCreated)
				data.entityToId.Dispose();

			if (data.pendingCreations.IsCreated)
				data.pendingCreations.Dispose();

			if (data.pendingDestructions.IsCreated)
				data.pendingDestructions.Dispose();

			if (data.pendingEntityIds.IsCreated)
				data.pendingEntityIds.Dispose();

			data = default;
		}

		/// <summary>
		/// Clears all registry data without disposing the collections.
		/// Use for test isolation - resets state between tests while keeping allocations.
		/// </summary>
		public static void Clear()
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return;

			if (data.idToEntity.IsCreated)
				data.idToEntity.Clear();

			if (data.entityToId.IsCreated)
				data.entityToId.Clear();

			if (data.pendingCreations.IsCreated)
				data.pendingCreations.Clear();

			if (data.pendingDestructions.IsCreated)
				data.pendingDestructions.Clear();

			if (data.pendingEntityIds.IsCreated)
				data.pendingEntityIds.Clear();

			data.nextId = 1;
		}

		/// <summary>
		/// O(1) lookup: ID → Entity (Burst-compatible).
		/// Returns Entity.Null if not found.
		/// </summary>
		public static Entity GetEntityFromId(int entityId)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated || entityId <= 0)
				return Entity.Null;

			if (data.pendingCreations.TryGetValue(entityId, out var pending))
				return pending;

			return data.idToEntity.TryGetValue(entityId, out var entity) ? entity : Entity.Null;
		}

		/// <summary>
		/// O(1) lookup: Entity → ID (Burst-compatible).
		/// Returns -1 if not found.
		/// </summary>
		public static int GetIdFromEntity(Entity entity)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated || entity == Entity.Null)
				return -1;

			return data.entityToId.TryGetValue(entity, out var id) ? id : -1;
		}

		/// <summary>
		/// O(1) check if ID exists.
		/// </summary>
		public static bool Contains(int entityId)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return false;

			return data.pendingCreations.ContainsKey(entityId) || data.idToEntity.ContainsKey(entityId);
		}

		/// <summary>
		/// O(1) check if Entity exists.
		/// </summary>
		public static bool Contains(Entity entity)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return false;

			return data.entityToId.ContainsKey(entity);
		}

		/// <summary>
		/// Returns true if the entity is pending (created this frame).
		/// </summary>
		public static bool IsPending(int entityId)
		{
			ref var data = ref s_registry.Data;
			return data.isCreated && data.pendingCreations.ContainsKey(entityId);
		}

		/// <summary>
		/// Returns true if the entity is marked for destruction.
		/// </summary>
		public static bool IsMarkedForDestruction(int entityId)
		{
			ref var data = ref s_registry.Data;
			return data.isCreated && data.pendingDestructions.Contains(entityId);
		}

		/// <summary>
		/// Allocates a new entity ID atomically.
		/// </summary>
		public static int AllocateId()
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return -1;

			return data.nextId++;
		}

		/// <summary>
		/// Ensures NextId is at least the given value.
		/// </summary>
		public static void EnsureNextId(int minNextId)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return;

			if (minNextId > data.nextId)
				data.nextId = minNextId;
		}

		/// <summary>
		/// Creates a new entity via ECB. Returns ID immediately.
		/// </summary>
		public static int Create(float3 position, EntityCommandBuffer ecb)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return -1;

			var id = data.nextId++;
			CreateWithId(id, position, ecb);
			return id;
		}

		/// <summary>
		/// Creates an entity with a pre-assigned ID.
		/// </summary>
		public static void CreateWithId(int id, float3 position, EntityCommandBuffer ecb)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return;

			if (id >= data.nextId)
				data.nextId = id + 1;

			var entity = ecb.CreateEntity();
			ecb.AddComponent(entity, LocalTransform.FromPosition(position));
			ecb.AddComponent(entity, new LuaEntityId { value = id });
			ecb.AddBuffer<LuaScriptRequest>(entity);
			ecb.AddBuffer<LuaEvent>(entity);

			data.pendingCreations[id] = entity;
		}

		/// <summary>
		/// Registers an existing entity. Returns the assigned ID.
		/// </summary>
		public static int Register(Entity entity, EntityCommandBuffer ecb)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return -1;

			if (data.entityToId.TryGetValue(entity, out var existingId))
				return existingId;

			var id = data.nextId++;
			ecb.AddComponent(entity, new LuaEntityId { value = id });

			data.idToEntity[id] = entity;
			data.entityToId[entity] = id;
			data.pendingCreations[id] = entity;

			return id;
		}

		/// <summary>
		/// Registers a baked entity with sentinel LuaEntityId (Value = 0).
		/// </summary>
		public static int RegisterBaked(Entity entity, EntityCommandBuffer ecb)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return -1;

			if (data.entityToId.TryGetValue(entity, out var existingId))
				return existingId;

			var id = data.nextId++;
			ecb.SetComponent(entity, new LuaEntityId { value = id });

			data.idToEntity[id] = entity;
			data.entityToId[entity] = id;
			data.pendingCreations[id] = entity;

			return id;
		}

		/// <summary>
		/// Registers an entity immediately (no ECB). Adds or updates LuaEntityId.
		/// </summary>
		public static void RegisterImmediate(Entity entity, int id, EntityManager entityManager)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return;

			if (id >= data.nextId)
				data.nextId = id + 1;

			data.idToEntity[id] = entity;
			data.entityToId[entity] = id;

			if (!entityManager.HasComponent<LuaEntityId>(entity))
			{
				entityManager.AddComponentData(entity, new LuaEntityId { value = id });
			}
			else
			{
				var luaId = entityManager.GetComponentData<LuaEntityId>(entity);
				if (luaId.value != id)
					entityManager.SetComponentData(entity, new LuaEntityId { value = id });
			}
		}

		/// <summary>
		/// Marks entity for destruction via ECB.
		/// </summary>
		public static bool Destroy(int entityId, EntityCommandBuffer ecb)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return false;

			if (data.pendingCreations.TryGetValue(entityId, out var pendingEntity))
			{
				ecb.DestroyEntity(pendingEntity);
				data.pendingCreations.Remove(entityId);
				return true;
			}

			if (!data.idToEntity.TryGetValue(entityId, out var entity))
				return false;

			ecb.RemoveComponent<LuaEntityId>(entity);
			data.pendingDestructions.Add(entityId);
			return true;
		}

		/// <summary>
		/// Commits pending creations after ECB playback.
		/// </summary>
		public static void CommitPendingCreations(EntityManager entityManager)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated || data.pendingCreations.Count == 0)
				return;

			var pendingIds = data.pendingCreations.GetKeyArray(Allocator.Temp);
			var pendingSet = new NativeHashSet<int>(pendingIds.Length, Allocator.Temp);
			foreach (var id in pendingIds)
				pendingSet.Add(id);

			var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LuaEntityId>());
			var entities = query.ToEntityArray(Allocator.Temp);
			var ids = query.ToComponentDataArray<LuaEntityId>(Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				var id = ids[i].value;
				if (pendingSet.Contains(id) && !data.idToEntity.ContainsKey(id))
				{
					data.idToEntity[id] = entities[i];
					data.entityToId[entities[i]] = id;
				}
			}

			entities.Dispose();
			ids.Dispose();
			pendingIds.Dispose();
			pendingSet.Dispose();
			data.pendingCreations.Clear();
		}

		/// <summary>
		/// Commits pending destructions after ECB playback.
		/// </summary>
		public static void CommitPendingDestructions()
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return;

			foreach (var id in data.pendingDestructions)
			{
				if (data.idToEntity.TryGetValue(id, out var entity))
				{
					data.entityToId.Remove(entity);
					data.idToEntity.Remove(id);
				}
			}

			data.pendingDestructions.Clear();
		}

		/// <summary>
		/// Syncs with world state, removing invalid entities.
		/// </summary>
		public static void SyncWithWorld(EntityManager entityManager)
		{
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return;

			var toRemove = new NativeList<int>(Allocator.Temp);

			foreach (var kvp in data.idToEntity)
			{
				if (!IsEntityValid(entityManager, kvp.Value, kvp.Key))
					toRemove.Add(kvp.Key);
			}

			foreach (var id in toRemove)
			{
				if (data.idToEntity.TryGetValue(id, out var entity))
				{
					data.entityToId.Remove(entity);
					data.idToEntity.Remove(id);
				}
			}

			toRemove.Dispose();
		}

		/// <summary>
		/// Gets all committed entity IDs.
		/// </summary>
		public static NativeArray<int> GetAllIds(Allocator allocator)
		{
			ref var data = ref s_registry.Data;
			return data.isCreated
				? data.idToEntity.GetKeyArray(allocator)
				: new NativeArray<int>(0, allocator);
		}

		/// <summary>
		/// Gets all committed entities.
		/// </summary>
		public static NativeArray<Entity> GetAllEntities(Allocator allocator)
		{
			ref var data = ref s_registry.Data;
			return data.isCreated
				? data.idToEntity.GetValueArray(allocator)
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

			var actualId = entityManager.GetComponentData<LuaEntityId>(entity).value;

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

			ref var data = ref s_registry.Data;
			if (data.isCreated && data.pendingEntityIds.IsCreated)
				data.pendingEntityIds.Clear();
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
			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return -1;

			var existingId = GetIdFromEntity(entity);
			if (existingId > 0)
				return existingId;

			if (data.pendingEntityIds.TryGetValue(entity, out var pendingId))
				return pendingId;

			if (entityManager.HasComponent<LuaEntityId>(entity))
			{
				var luaId = entityManager.GetComponentData<LuaEntityId>(entity);
				if (luaId.value == 0)
				{
					var id = RegisterBaked(entity, ecb);
					data.pendingEntityIds[entity] = id;
					return id;
				}
			}

			var newId = Register(entity, ecb);
			data.pendingEntityIds[entity] = newId;
			return newId;
		}

		/// <summary>
		/// O(1) lookup: Entity → ID with pending fallback.
		/// </summary>
		public static int GetEntityIdFromEntity(Entity entity, EntityManager entityManager)
		{
			if (entity == Entity.Null || !entityManager.Exists(entity))
				return -1;

			ref var data = ref s_registry.Data;
			if (!data.isCreated)
				return -1;

			var id = GetIdFromEntity(entity);
			if (id > 0)
				return id;

			if (data.pendingEntityIds.TryGetValue(entity, out var pendingId))
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
				Log.Warning("[LuaEntityRegistry] AddScriptDeferred: entity {0} not found", entityId);
				return false;
			}

			var request = new LuaScriptRequest
			{
				scriptName = scriptName,
				requestHash = LuaScriptPathUtility.HashScriptName(scriptName),
				fulfilled = false,
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
