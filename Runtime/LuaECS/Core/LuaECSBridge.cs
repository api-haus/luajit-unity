namespace LuaECS.Core
{
	using System.Threading;
	using Components;
	using LuaNET.LuaJIT;
	using Systems;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;

	public struct PendingEntityCreation
	{
		public int entityId;
		public float3 position;
	}

	public struct PendingScriptAddition
	{
		public int entityId;
		public FixedString64Bytes scriptName;
	}

	/// <summary>
	/// Unmanaged struct for pending event dispatch information.
	/// </summary>
	public struct PendingEventDispatch
	{
		public Entity entity;
		public int scriptIndex;
		public FixedString64Bytes scriptName;
		public int entityIndex;
		public int stateRef;
		public int eventStartIndex;
		public int eventCount;
	}

	/// <summary>
	/// Unmanaged event context for SharedStatic storage.
	/// </summary>
	public struct LuaEventContextData
	{
		public UnsafeList<PendingEventDispatch> pendingEvents;
		public UnsafeList<LuaEvent> eventBuffer;
		public UnsafeList<Entity> entitiesToClear;
		public bool isValid;
	}

	/// <summary>
	/// Unmanaged context for Burst-compiled Lua bridge functions.
	/// Updated each frame before script execution.
	/// </summary>
	public unsafe struct BurstBridgeContext
	{
		/// <summary>
		/// Direct ECB for deferred entity operations. Set each frame from EndSimulationEntityCommandBufferSystem.
		/// </summary>
		public EntityCommandBuffer ecb;

		[NativeDisableUnsafePtrRestriction]
		public UnsafeHashMap<int, Entity> entityIdMap;

		[NativeDisableUnsafePtrRestriction]
		public ComponentLookup<LocalTransform> transformLookup;

		[NativeDisableUnsafePtrRestriction]
		public BufferLookup<LuaScript> scriptBufferLookup;

		/// <summary>
		/// Delta time for the current frame, used by movement functions.
		/// </summary>
		public float deltaTime;

		public bool isValid;
	}

	/// <summary>
	/// Coordinates Lua-to-ECS bridge functions.
	/// Static state required for Burst-compatible [MonoPInvokeCallback] methods.
	/// Domain-specific functions are organized in partial classes under Bridge/.
	/// </summary>
	public static partial class LuaECSBridge
	{
		struct BurstContextMarker { }

		struct NextEntityIdMarker { }

		struct EventContextMarker { }

		struct PendingEntitiesMarker { }

		static World s_world;
		static EntityManager s_entityManager;
		static EntityQuery s_playerQuery;
		static bool s_playerQueryInitialized;
		static bool s_initialized;

		/// <summary>
		/// Tracks entities created in the current frame before ECB playback.
		/// Maps entityId to deferred Entity handle for same-frame script additions.
		/// </summary>
		static readonly SharedStatic<UnsafeHashMap<int, Entity>> s_pendingEntities =
			SharedStatic<UnsafeHashMap<int, Entity>>.GetOrCreate<PendingEntitiesMarker, UnsafeHashMap<int, Entity>>();

		static readonly SharedStatic<BurstBridgeContext> s_burstContext =
			SharedStatic<BurstBridgeContext>.GetOrCreate<BurstContextMarker, BurstBridgeContext>();

		static readonly SharedStatic<int> s_nextEntityId =
			SharedStatic<int>.GetOrCreate<NextEntityIdMarker>();

		static readonly SharedStatic<LuaEventContextData> s_eventContext =
			SharedStatic<LuaEventContextData>.GetOrCreate<EventContextMarker, LuaEventContextData>();

		public static ref LuaEventContextData EventContext => ref s_eventContext.Data;

		public static void Initialize(World world, LuaScriptingSystem scriptingSystem)
		{
			if (s_initialized)
			{
				s_world = world;
				s_entityManager = world.EntityManager;
				return;
			}

			s_world = world;
			s_entityManager = world.EntityManager;
			s_nextEntityId.Data = 1;

			// Initialize pending entities map for same-frame entity tracking
			s_pendingEntities.Data = new UnsafeHashMap<int, Entity>(32, Allocator.Persistent);

			s_eventContext.Data = new LuaEventContextData
			{
				pendingEvents = new UnsafeList<PendingEventDispatch>(64, Allocator.Persistent),
				eventBuffer = new UnsafeList<LuaEvent>(128, Allocator.Persistent),
				entitiesToClear = new UnsafeList<Entity>(64, Allocator.Persistent),
				isValid = true,
			};

			if (s_playerQueryInitialized)
				s_playerQuery.Dispose();

			s_playerQuery = s_entityManager.CreateEntityQuery(ComponentType.ReadOnly<LuaPlayerTag>());
			s_playerQueryInitialized = true;
			s_initialized = true;
		}

		public static void Shutdown()
		{
			s_burstContext.Data = default;

			// Dispose pending entities map
			if (s_pendingEntities.Data.IsCreated)
				s_pendingEntities.Data.Dispose();

			ref var eventCtx = ref s_eventContext.Data;
			if (eventCtx.isValid)
			{
				if (eventCtx.pendingEvents.IsCreated)
					eventCtx.pendingEvents.Dispose();
				if (eventCtx.eventBuffer.IsCreated)
					eventCtx.eventBuffer.Dispose();
				if (eventCtx.entitiesToClear.IsCreated)
					eventCtx.entitiesToClear.Dispose();
				eventCtx = default;
			}

			if (s_playerQueryInitialized)
			{
				s_playerQuery.Dispose();
				s_playerQueryInitialized = false;
			}

			s_playerQuery = default;

			s_world = null;
			s_entityManager = default;
			s_initialized = false;
		}

		/// <summary>
		/// Updates the Burst-compatible context with current frame data.
		/// Call before executing Lua scripts each frame.
		/// </summary>
		/// <param name="ecb">ECB from EndSimulationEntityCommandBufferSystem for deferred operations</param>
		/// <param name="deltaTime">Delta time for the current frame</param>
		/// <param name="transformLookup">Transform component lookup</param>
		/// <param name="scriptBufferLookup">Script buffer lookup</param>
		public static void UpdateBurstContext(
			EntityCommandBuffer ecb,
			float deltaTime,
			ComponentLookup<LocalTransform> transformLookup,
			BufferLookup<LuaScript> scriptBufferLookup
		)
		{
			if (!s_initialized)
			{
				s_burstContext.Data = default;
				return;
			}

			if (!LuaEntityRegistry.IsCreated)
			{
				s_burstContext.Data = default;
				return;
			}

			// Clear pending entities from previous frame
			if (s_pendingEntities.Data.IsCreated)
				s_pendingEntities.Data.Clear();

			s_burstContext.Data = new BurstBridgeContext
			{
				ecb = ecb,
				deltaTime = deltaTime,
				entityIdMap = LuaEntityRegistry.EntityIdMap,
				transformLookup = transformLookup,
				scriptBufferLookup = scriptBufferLookup,
				isValid = true,
			};
		}

		/// <summary>
		/// Adds an entity to the pending map for same-frame tracking.
		/// Called when creating entities via ECB.
		/// </summary>
		internal static void AddPendingEntity(int entityId, Entity entity)
		{
			if (s_pendingEntities.Data.IsCreated)
				s_pendingEntities.Data.TryAdd(entityId, entity);
		}

		/// <summary>
		/// Gets a pending entity by ID (for same-frame operations before ECB playback).
		/// </summary>
		internal static Entity GetPendingEntity(int entityId)
		{
			if (s_pendingEntities.Data.IsCreated && s_pendingEntities.Data.TryGetValue(entityId, out var entity))
				return entity;
			return Entity.Null;
		}

		public static void RegisterFunctions(lua_State l)
		{
			// Domain-oriented namespaces
			RegisterEntitiesFunctions(l); // entities.*
			RegisterTransformNamespace(l); // transform.*
			RegisterSpatialNamespace(l); // spatial.*
			RegisterEventsFunctions(l); // events.*

			InitializeGlobalLog(l);

			RegisterInputFunctions(l);
			RegisterCharacterFunctions(l);
			RegisterDrawFunctions(l);
			RegisterPlayerFunctions(l);
		}

		static void RegisterFunction(lua_State l, string name, Lua.lua_CFunction func)
		{
			Lua.lua_pushcfunction(l, func);
			Lua.lua_setfield(l, -2, name);
		}

		static float3 TableToFloat3(lua_State l, int index)
		{
			var result = float3.zero;

			if (index < 0)
				index = Lua.lua_gettop(l) + index + 1;

			Lua.lua_getfield(l, index, "x");
			if (Lua.lua_isnumber(l, -1) != 0)
				result.x = (float)Lua.lua_tonumber(l, -1);
			Lua.lua_pop(l, 1);

			Lua.lua_getfield(l, index, "y");
			if (Lua.lua_isnumber(l, -1) != 0)
				result.y = (float)Lua.lua_tonumber(l, -1);
			Lua.lua_pop(l, 1);

			Lua.lua_getfield(l, index, "z");
			if (Lua.lua_isnumber(l, -1) != 0)
				result.z = (float)Lua.lua_tonumber(l, -1);
			Lua.lua_pop(l, 1);

			return result;
		}

		static float3 QuaternionToEuler(quaternion q)
		{
			float3 euler;

			var sinrCosp = 2 * ((q.value.w * q.value.x) + (q.value.y * q.value.z));
			var cosrCosp = 1 - (2 * ((q.value.x * q.value.x) + (q.value.y * q.value.y)));
			euler.x = math.atan2(sinrCosp, cosrCosp);

			var sinp = 2 * ((q.value.w * q.value.y) - (q.value.z * q.value.x));
			if (math.abs(sinp) >= 1)
				euler.y = math.sign(sinp) * math.PI / 2;
			else
				euler.y = math.asin(sinp);

			var sinyCosp = 2 * ((q.value.w * q.value.z) + (q.value.x * q.value.y));
			var cosyCosp = 1 - (2 * ((q.value.y * q.value.y) + (q.value.z * q.value.z)));
			euler.z = math.atan2(sinyCosp, cosyCosp);

			return math.degrees(euler);
		}

		/// <summary>
		/// Burst-compatible entity lookup from entity ID.
		/// Returns Entity.Null if not found.
		/// </summary>
		public static Entity GetEntityFromIdBurst(int entityId)
		{
			ref var ctx = ref s_burstContext.Data;
			if (!ctx.isValid || entityId <= 0)
				return Entity.Null;

			return ctx.entityIdMap.TryGetValue(entityId, out var entity) ? entity : Entity.Null;
		}

		/// <summary>
		/// Burst-compatible check if transform exists and get value.
		/// </summary>
		internal static bool TryGetTransformBurst(Entity entity, out LocalTransform transform)
		{
			ref var ctx = ref s_burstContext.Data;
			transform = default;

			if (!ctx.isValid || entity == Entity.Null)
				return false;

			if (!ctx.transformLookup.HasComponent(entity))
				return false;

			transform = ctx.transformLookup[entity];
			return true;
		}

		/// <summary>
		/// Burst-compatible set transform.
		/// </summary>
		internal static bool TrySetTransformBurst(Entity entity, LocalTransform transform)
		{
			ref var ctx = ref s_burstContext.Data;

			if (!ctx.isValid || entity == Entity.Null)
				return false;

			if (!ctx.transformLookup.HasComponent(entity))
				return false;

			ctx.transformLookup[entity] = transform;
			return true;
		}

		/// <summary>
		/// Allocate a new entity ID atomically.
		/// Used for synchronous entity creation (e.g., character.create).
		/// </summary>
		internal static int AllocateEntityId()
		{
			return Interlocked.Increment(ref s_nextEntityId.Data);
		}

		/// <summary>
		/// Burst-compatible script lookup via BufferLookup.
		/// </summary>
		internal static bool HasScriptBurst(Entity entity, FixedString64Bytes scriptName)
		{
			ref var ctx = ref s_burstContext.Data;
			if (!ctx.isValid || entity == Entity.Null)
				return false;

			if (!ctx.scriptBufferLookup.HasBuffer(entity))
				return false;

			var scripts = ctx.scriptBufferLookup[entity];
			for (var i = 0; i < scripts.Length; i++)
			{
				if (scripts[i].scriptName == scriptName)
					return true;
			}

			return false;
		}

		/// <summary>
		/// Syncs the bridge's entity ID counter with the collection's current state.
		/// Call after external entity creation to prevent ID collisions.
		/// </summary>
		public static void SyncNextEntityId(int nextId)
		{
			var current = s_nextEntityId.Data;
			while (current < nextId)
			{
				var prev = Interlocked.CompareExchange(ref s_nextEntityId.Data, nextId, current);
				if (prev == current)
					break;
				current = prev;
			}
		}

		/// <summary>
		/// Clears the event context for a new frame.
		/// </summary>
		public static void ClearEventContext()
		{
			ref var ctx = ref s_eventContext.Data;
			if (!ctx.isValid)
				return;

			ctx.pendingEvents.Clear();
			ctx.eventBuffer.Clear();
			ctx.entitiesToClear.Clear();
		}

		/// <summary>
		/// Adds an event dispatch entry to the context.
		/// </summary>
		public static void AddEventDispatch(
			Entity entity,
			int scriptIndex,
			FixedString64Bytes scriptName,
			int entityIndex,
			int stateRef,
			int eventStartIndex,
			int eventCount
		)
		{
			ref var ctx = ref s_eventContext.Data;
			if (!ctx.isValid)
				return;

			ctx.pendingEvents.Add(
				new PendingEventDispatch
				{
					entity = entity,
					scriptIndex = scriptIndex,
					scriptName = scriptName,
					entityIndex = entityIndex,
					stateRef = stateRef,
					eventStartIndex = eventStartIndex,
					eventCount = eventCount,
				}
			);
		}

		/// <summary>
		/// Adds an event to the event buffer.
		/// </summary>
		public static int AddEvent(LuaEvent evt)
		{
			ref var ctx = ref s_eventContext.Data;
			if (!ctx.isValid)
				return -1;

			var index = ctx.eventBuffer.Length;
			ctx.eventBuffer.Add(evt);
			return index;
		}

		/// <summary>
		/// Adds an entity to the clear list.
		/// </summary>
		public static void AddEntityToClear(Entity entity)
		{
			ref var ctx = ref s_eventContext.Data;
			if (!ctx.isValid)
				return;

			ctx.entitiesToClear.Add(entity);
		}

		/// <summary>
		/// Gets an event from the buffer by index.
		/// </summary>
		public static LuaEvent GetEvent(int index)
		{
			ref var ctx = ref s_eventContext.Data;
			if (!ctx.isValid || index < 0 || index >= ctx.eventBuffer.Length)
				return default;

			return ctx.eventBuffer[index];
		}
	}
}
