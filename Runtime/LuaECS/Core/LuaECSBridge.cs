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
		[NativeDisableUnsafePtrRestriction]
		public UnsafeHashMap<int, Entity> entityIdMap;

		[NativeDisableUnsafePtrRestriction]
		public ComponentLookup<LocalTransform> transformLookup;

		[NativeDisableUnsafePtrRestriction]
		public BufferLookup<LuaScript> scriptBufferLookup;

		[NativeDisableUnsafePtrRestriction]
		public UnsafeList<LuaCommand>* pendingCommands;

		[NativeDisableUnsafePtrRestriction]
		public UnsafeList<int>* pendingDestructions;

		[NativeDisableUnsafePtrRestriction]
		public UnsafeList<PendingEntityCreation>* pendingCreations;

		[NativeDisableUnsafePtrRestriction]
		public UnsafeList<PendingScriptAddition>* pendingScripts;

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

		static World s_world;
		static EntityManager s_entityManager;
		static LuaScriptingSystem s_scriptingSystem;
		static EntityQuery s_playerQuery;
		static bool s_playerQueryInitialized;

		static NativeList<LuaCommand> s_pendingCommands;
		static NativeList<int> s_pendingDestructions;
		static NativeList<PendingEntityCreation> s_pendingCreations;
		static NativeList<PendingScriptAddition> s_pendingScripts;
		static bool s_initialized;

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
				s_scriptingSystem = scriptingSystem;
				return;
			}

			s_world = world;
			s_entityManager = world.EntityManager;
			s_scriptingSystem = scriptingSystem;
			s_pendingCommands = new NativeList<LuaCommand>(64, Allocator.Persistent);
			s_pendingDestructions = new NativeList<int>(32, Allocator.Persistent);
			s_pendingCreations = new NativeList<PendingEntityCreation>(32, Allocator.Persistent);
			s_pendingScripts = new NativeList<PendingScriptAddition>(32, Allocator.Persistent);
			s_nextEntityId.Data = 1;

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

			if (s_pendingCommands.IsCreated)
				s_pendingCommands.Dispose();

			if (s_pendingDestructions.IsCreated)
				s_pendingDestructions.Dispose();

			if (s_pendingCreations.IsCreated)
				s_pendingCreations.Dispose();

			if (s_pendingScripts.IsCreated)
				s_pendingScripts.Dispose();

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
			s_scriptingSystem = null;
			s_initialized = false;
		}

		/// <summary>
		/// Updates the Burst-compatible context with current frame data.
		/// Call before executing Lua scripts each frame.
		/// </summary>
		public static void UpdateBurstContext(
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

			unsafe
			{
				s_burstContext.Data = new BurstBridgeContext
				{
					entityIdMap = LuaEntityRegistry.EntityIdMap,
					transformLookup = transformLookup,
					scriptBufferLookup = scriptBufferLookup,
					pendingCommands = s_pendingCommands.GetUnsafeList(),
					pendingDestructions = s_pendingDestructions.GetUnsafeList(),
					pendingCreations = s_pendingCreations.GetUnsafeList(),
					pendingScripts = s_pendingScripts.GetUnsafeList(),
					isValid = true,
				};
			}
		}

		public static void RegisterFunctions(lua_State l)
		{
			Lua.lua_newtable(l);

			RegisterTransformFunctions(l);
			RegisterSpatialFunctions(l);
			RegisterEntityFunctions(l);
			RegisterCommandFunctions(l);
			RegisterLogFunctions(l);

			Lua.lua_setglobal(l, "ecs");

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

		public static NativeList<LuaCommand> FlushCommands()
		{
			if (!s_initialized || !s_pendingCommands.IsCreated)
			{
				return new NativeList<LuaCommand>(0, Allocator.Temp);
			}

			var commands = new NativeList<LuaCommand>(s_pendingCommands.Length, Allocator.Temp);
			commands.CopyFrom(s_pendingCommands);
			s_pendingCommands.Clear();
			return commands;
		}

		static Entity GetEntityFromId(int entityId)
		{
			return LuaEntityRegistry.GetEntityFromId(entityId);
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
		/// Burst-compatible command queue addition.
		/// </summary>
		internal static unsafe void AddCommandBurst(LuaCommand command)
		{
			ref var ctx = ref s_burstContext.Data;
			if (!ctx.isValid || ctx.pendingCommands == null)
				return;

			ctx.pendingCommands->Add(command);
		}

		/// <summary>
		/// Burst-compatible entity destruction queue addition.
		/// </summary>
		internal static unsafe void QueueDestructionBurst(int entityId)
		{
			ref var ctx = ref s_burstContext.Data;
			if (!ctx.isValid || ctx.pendingDestructions == null || entityId <= 0)
				return;

			ctx.pendingDestructions->Add(entityId);
		}

		/// <summary>
		/// Flushes pending destructions and returns them.
		/// Call from managed system after script execution.
		/// </summary>
		public static NativeList<int> FlushDestructions()
		{
			if (!s_initialized || !s_pendingDestructions.IsCreated)
				return new NativeList<int>(0, Allocator.Temp);

			var destructions = new NativeList<int>(s_pendingDestructions.Length, Allocator.Temp);
			destructions.CopyFrom(s_pendingDestructions);
			s_pendingDestructions.Clear();
			return destructions;
		}

		/// <summary>
		/// Burst-compatible entity creation. Returns new entity ID atomically.
		/// </summary>
		internal static unsafe int CreateEntityBurst(float3 position)
		{
			ref var ctx = ref s_burstContext.Data;
			if (!ctx.isValid || ctx.pendingCreations == null)
				return -1;

			var entityId = Interlocked.Increment(ref s_nextEntityId.Data);
			ctx.pendingCreations->Add(
				new PendingEntityCreation { entityId = entityId, position = position }
			);
			return entityId;
		}

		/// <summary>
		/// Allocate a new entity ID atomically (for non-deferred entity creation).
		/// </summary>
		internal static int AllocateEntityId()
		{
			return Interlocked.Increment(ref s_nextEntityId.Data);
		}

		/// <summary>
		/// Burst-compatible script addition. Returns false if context invalid.
		/// </summary>
		internal static unsafe bool AddScriptBurst(int entityId, FixedString64Bytes scriptName)
		{
			ref var ctx = ref s_burstContext.Data;
			if (!ctx.isValid || ctx.pendingScripts == null || entityId <= 0)
				return false;

			ctx.pendingScripts->Add(
				new PendingScriptAddition { entityId = entityId, scriptName = scriptName }
			);
			return true;
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
		/// Flushes pending entity creations.
		/// </summary>
		public static NativeList<PendingEntityCreation> FlushCreations()
		{
			if (!s_initialized || !s_pendingCreations.IsCreated)
				return new NativeList<PendingEntityCreation>(0, Allocator.Temp);

			var creations = new NativeList<PendingEntityCreation>(
				s_pendingCreations.Length,
				Allocator.Temp
			);
			creations.CopyFrom(s_pendingCreations);
			s_pendingCreations.Clear();
			return creations;
		}

		/// <summary>
		/// Flushes pending script additions.
		/// </summary>
		public static NativeList<PendingScriptAddition> FlushScriptAdditions()
		{
			if (!s_initialized || !s_pendingScripts.IsCreated)
				return new NativeList<PendingScriptAddition>(0, Allocator.Temp);

			var scripts = new NativeList<PendingScriptAddition>(s_pendingScripts.Length, Allocator.Temp);
			scripts.CopyFrom(s_pendingScripts);
			s_pendingScripts.Clear();
			return scripts;
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
