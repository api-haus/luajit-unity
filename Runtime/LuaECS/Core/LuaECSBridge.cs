namespace LuaECS.Core
{
	using System.Threading;
	using LuaECS.Components;
	using LuaECS.Systems;
	using LuaNET.LuaJIT;
	using LuaVM.Burst;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;

	public struct PendingEntityCreation
	{
		public int EntityId;
		public float3 Position;
	}

	public struct PendingScriptAddition
	{
		public int EntityId;
		public FixedString64Bytes ScriptName;
	}

	/// <summary>
	/// Unmanaged struct for pending event dispatch information.
	/// </summary>
	public struct PendingEventDispatch
	{
		public Entity Entity;
		public int ScriptIndex;
		public FixedString64Bytes ScriptName;
		public int EntityIndex;
		public int StateRef;
		public int EventStartIndex;
		public int EventCount;
	}

	/// <summary>
	/// Unmanaged event context for SharedStatic storage.
	/// </summary>
	public struct LuaEventContextData
	{
		public UnsafeList<PendingEventDispatch> PendingEvents;
		public UnsafeList<LuaEvent> EventBuffer;
		public UnsafeList<Entity> EntitiesToClear;
		public bool IsValid;
	}

	/// <summary>
	/// Unmanaged context for Burst-compiled Lua bridge functions.
	/// Updated each frame before script execution.
	/// </summary>
	public unsafe struct BurstBridgeContext
	{
		[NativeDisableUnsafePtrRestriction]
		public UnsafeHashMap<int, Entity> EntityIdMap;

		[NativeDisableUnsafePtrRestriction]
		public ComponentLookup<LocalTransform> TransformLookup;

		[NativeDisableUnsafePtrRestriction]
		public BufferLookup<LuaScript> ScriptBufferLookup;

		[NativeDisableUnsafePtrRestriction]
		public UnsafeList<LuaCommand>* PendingCommands;

		[NativeDisableUnsafePtrRestriction]
		public UnsafeList<int>* PendingDestructions;

		[NativeDisableUnsafePtrRestriction]
		public UnsafeList<PendingEntityCreation>* PendingCreations;

		[NativeDisableUnsafePtrRestriction]
		public UnsafeList<PendingScriptAddition>* PendingScripts;

		public bool IsValid;
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

		static World s_World;
		static EntityManager s_EntityManager;
		static LuaScriptingSystem s_ScriptingSystem;
		static EntityQuery s_PlayerQuery;
		static bool s_PlayerQueryInitialized;

		static NativeList<LuaCommand> s_PendingCommands;
		static NativeList<int> s_PendingDestructions;
		static NativeList<PendingEntityCreation> s_PendingCreations;
		static NativeList<PendingScriptAddition> s_PendingScripts;
		static bool s_Initialized;

		static readonly SharedStatic<BurstBridgeContext> s_BurstContext =
			SharedStatic<BurstBridgeContext>.GetOrCreate<BurstContextMarker, BurstBridgeContext>();

		static readonly SharedStatic<int> s_NextEntityId =
			SharedStatic<int>.GetOrCreate<NextEntityIdMarker>();

		static readonly SharedStatic<LuaEventContextData> s_EventContext =
			SharedStatic<LuaEventContextData>.GetOrCreate<EventContextMarker, LuaEventContextData>();

		public static ref LuaEventContextData EventContext => ref s_EventContext.Data;

		public static void Initialize(World world, LuaScriptingSystem scriptingSystem)
		{
			if (s_Initialized)
			{
				s_World = world;
				s_EntityManager = world.EntityManager;
				s_ScriptingSystem = scriptingSystem;
				return;
			}

			s_World = world;
			s_EntityManager = world.EntityManager;
			s_ScriptingSystem = scriptingSystem;
			s_PendingCommands = new NativeList<LuaCommand>(64, Allocator.Persistent);
			s_PendingDestructions = new NativeList<int>(32, Allocator.Persistent);
			s_PendingCreations = new NativeList<PendingEntityCreation>(32, Allocator.Persistent);
			s_PendingScripts = new NativeList<PendingScriptAddition>(32, Allocator.Persistent);
			s_NextEntityId.Data = 1;

			unsafe
			{
				s_EventContext.Data = new LuaEventContextData
				{
					PendingEvents = new UnsafeList<PendingEventDispatch>(64, Allocator.Persistent),
					EventBuffer = new UnsafeList<LuaEvent>(128, Allocator.Persistent),
					EntitiesToClear = new UnsafeList<Entity>(64, Allocator.Persistent),
					IsValid = true,
				};
			}

			if (s_PlayerQueryInitialized)
				s_PlayerQuery.Dispose();

			s_PlayerQuery = s_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LuaPlayerTag>());
			s_PlayerQueryInitialized = true;
			s_Initialized = true;
		}

		public static void Shutdown()
		{
			s_BurstContext.Data = default;

			if (s_PendingCommands.IsCreated)
				s_PendingCommands.Dispose();

			if (s_PendingDestructions.IsCreated)
				s_PendingDestructions.Dispose();

			if (s_PendingCreations.IsCreated)
				s_PendingCreations.Dispose();

			if (s_PendingScripts.IsCreated)
				s_PendingScripts.Dispose();

			ref var eventCtx = ref s_EventContext.Data;
			if (eventCtx.IsValid)
			{
				if (eventCtx.PendingEvents.IsCreated)
					eventCtx.PendingEvents.Dispose();
				if (eventCtx.EventBuffer.IsCreated)
					eventCtx.EventBuffer.Dispose();
				if (eventCtx.EntitiesToClear.IsCreated)
					eventCtx.EntitiesToClear.Dispose();
				eventCtx = default;
			}

			if (s_PlayerQueryInitialized)
			{
				s_PlayerQuery.Dispose();
				s_PlayerQueryInitialized = false;
			}
			s_PlayerQuery = default;

			s_World = null;
			s_EntityManager = default;
			s_ScriptingSystem = null;
			s_Initialized = false;
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
			if (!s_Initialized)
			{
				s_BurstContext.Data = default;
				return;
			}

			if (!LuaEntityRegistry.IsCreated)
			{
				s_BurstContext.Data = default;
				return;
			}

			unsafe
			{
				s_BurstContext.Data = new BurstBridgeContext
				{
					EntityIdMap = LuaEntityRegistry.EntityIdMap,
					TransformLookup = transformLookup,
					ScriptBufferLookup = scriptBufferLookup,
					PendingCommands = s_PendingCommands.GetUnsafeList(),
					PendingDestructions = s_PendingDestructions.GetUnsafeList(),
					PendingCreations = s_PendingCreations.GetUnsafeList(),
					PendingScripts = s_PendingScripts.GetUnsafeList(),
					IsValid = true,
				};
			}
		}

		public static void RegisterFunctions(lua_State L)
		{
			Lua.lua_newtable(L);

			RegisterTransformFunctions(L);
			RegisterSpatialFunctions(L);
			RegisterEntityFunctions(L);
			RegisterCommandFunctions(L);
			RegisterLogFunctions(L);

			Lua.lua_setglobal(L, "ecs");

			InitializeGlobalLog(L);

			RegisterInputFunctions(L);
			RegisterCharacterFunctions(L);
			RegisterDrawFunctions(L);
			RegisterPlayerFunctions(L);
		}

		static void RegisterFunction(lua_State L, string name, Lua.lua_CFunction func)
		{
			Lua.lua_pushcfunction(L, func);
			Lua.lua_setfield(L, -2, name);
		}

		public static NativeList<LuaCommand> FlushCommands()
		{
			if (!s_Initialized || !s_PendingCommands.IsCreated)
			{
				return new NativeList<LuaCommand>(0, Allocator.Temp);
			}

			var commands = new NativeList<LuaCommand>(s_PendingCommands.Length, Allocator.Temp);
			commands.CopyFrom(s_PendingCommands);
			s_PendingCommands.Clear();
			return commands;
		}

		static Entity GetEntityFromId(int entityId)
		{
			return LuaEntityRegistry.GetEntityFromId(entityId);
		}

		static float3 TableToFloat3(lua_State L, int index)
		{
			var result = float3.zero;

			if (index < 0)
				index = Lua.lua_gettop(L) + index + 1;

			Lua.lua_getfield(L, index, "x");
			if (Lua.lua_isnumber(L, -1) != 0)
				result.x = (float)Lua.lua_tonumber(L, -1);
			Lua.lua_pop(L, 1);

			Lua.lua_getfield(L, index, "y");
			if (Lua.lua_isnumber(L, -1) != 0)
				result.y = (float)Lua.lua_tonumber(L, -1);
			Lua.lua_pop(L, 1);

			Lua.lua_getfield(L, index, "z");
			if (Lua.lua_isnumber(L, -1) != 0)
				result.z = (float)Lua.lua_tonumber(L, -1);
			Lua.lua_pop(L, 1);

			return result;
		}

		static float3 QuaternionToEuler(quaternion q)
		{
			float3 euler;

			var sinr_cosp = 2 * (q.value.w * q.value.x + q.value.y * q.value.z);
			var cosr_cosp = 1 - 2 * (q.value.x * q.value.x + q.value.y * q.value.y);
			euler.x = math.atan2(sinr_cosp, cosr_cosp);

			var sinp = 2 * (q.value.w * q.value.y - q.value.z * q.value.x);
			if (math.abs(sinp) >= 1)
				euler.y = math.sign(sinp) * math.PI / 2;
			else
				euler.y = math.asin(sinp);

			var siny_cosp = 2 * (q.value.w * q.value.z + q.value.x * q.value.y);
			var cosy_cosp = 1 - 2 * (q.value.y * q.value.y + q.value.z * q.value.z);
			euler.z = math.atan2(siny_cosp, cosy_cosp);

			return math.degrees(euler);
		}

		/// <summary>
		/// Burst-compatible entity lookup from entity ID.
		/// Returns Entity.Null if not found.
		/// </summary>
		public static Entity GetEntityFromIdBurst(int entityId)
		{
			ref var ctx = ref s_BurstContext.Data;
			if (!ctx.IsValid || entityId <= 0)
				return Entity.Null;

			return ctx.EntityIdMap.TryGetValue(entityId, out var entity) ? entity : Entity.Null;
		}

		/// <summary>
		/// Burst-compatible check if transform exists and get value.
		/// </summary>
		internal static bool TryGetTransformBurst(Entity entity, out LocalTransform transform)
		{
			ref var ctx = ref s_BurstContext.Data;
			transform = default;

			if (!ctx.IsValid || entity == Entity.Null)
				return false;

			if (!ctx.TransformLookup.HasComponent(entity))
				return false;

			transform = ctx.TransformLookup[entity];
			return true;
		}

		/// <summary>
		/// Burst-compatible set transform.
		/// </summary>
		internal static bool TrySetTransformBurst(Entity entity, LocalTransform transform)
		{
			ref var ctx = ref s_BurstContext.Data;

			if (!ctx.IsValid || entity == Entity.Null)
				return false;

			if (!ctx.TransformLookup.HasComponent(entity))
				return false;

			ctx.TransformLookup[entity] = transform;
			return true;
		}

		/// <summary>
		/// Burst-compatible command queue addition.
		/// </summary>
		internal static unsafe void AddCommandBurst(LuaCommand command)
		{
			ref var ctx = ref s_BurstContext.Data;
			if (!ctx.IsValid || ctx.PendingCommands == null)
				return;

			ctx.PendingCommands->Add(command);
		}

		/// <summary>
		/// Burst-compatible entity destruction queue addition.
		/// </summary>
		internal static unsafe void QueueDestructionBurst(int entityId)
		{
			ref var ctx = ref s_BurstContext.Data;
			if (!ctx.IsValid || ctx.PendingDestructions == null || entityId <= 0)
				return;

			ctx.PendingDestructions->Add(entityId);
		}

		/// <summary>
		/// Flushes pending destructions and returns them.
		/// Call from managed system after script execution.
		/// </summary>
		public static NativeList<int> FlushDestructions()
		{
			if (!s_Initialized || !s_PendingDestructions.IsCreated)
				return new NativeList<int>(0, Allocator.Temp);

			var destructions = new NativeList<int>(s_PendingDestructions.Length, Allocator.Temp);
			destructions.CopyFrom(s_PendingDestructions);
			s_PendingDestructions.Clear();
			return destructions;
		}

		/// <summary>
		/// Burst-compatible entity creation. Returns new entity ID atomically.
		/// </summary>
		internal static unsafe int CreateEntityBurst(float3 position)
		{
			ref var ctx = ref s_BurstContext.Data;
			if (!ctx.IsValid || ctx.PendingCreations == null)
				return -1;

			var entityId = Interlocked.Increment(ref s_NextEntityId.Data);
			ctx.PendingCreations->Add(
				new PendingEntityCreation { EntityId = entityId, Position = position }
			);
			return entityId;
		}

		/// <summary>
		/// Allocate a new entity ID atomically (for non-deferred entity creation).
		/// </summary>
		internal static int AllocateEntityId()
		{
			return Interlocked.Increment(ref s_NextEntityId.Data);
		}

		/// <summary>
		/// Burst-compatible script addition. Returns false if context invalid.
		/// </summary>
		internal static unsafe bool AddScriptBurst(int entityId, FixedString64Bytes scriptName)
		{
			ref var ctx = ref s_BurstContext.Data;
			if (!ctx.IsValid || ctx.PendingScripts == null || entityId <= 0)
				return false;

			ctx.PendingScripts->Add(
				new PendingScriptAddition { EntityId = entityId, ScriptName = scriptName }
			);
			return true;
		}

		/// <summary>
		/// Burst-compatible script lookup via BufferLookup.
		/// </summary>
		internal static bool HasScriptBurst(Entity entity, FixedString64Bytes scriptName)
		{
			ref var ctx = ref s_BurstContext.Data;
			if (!ctx.IsValid || entity == Entity.Null)
				return false;

			if (!ctx.ScriptBufferLookup.HasBuffer(entity))
				return false;

			var scripts = ctx.ScriptBufferLookup[entity];
			for (var i = 0; i < scripts.Length; i++)
			{
				if (scripts[i].ScriptName == scriptName)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Flushes pending entity creations.
		/// </summary>
		public static NativeList<PendingEntityCreation> FlushCreations()
		{
			if (!s_Initialized || !s_PendingCreations.IsCreated)
				return new NativeList<PendingEntityCreation>(0, Allocator.Temp);

			var creations = new NativeList<PendingEntityCreation>(
				s_PendingCreations.Length,
				Allocator.Temp
			);
			creations.CopyFrom(s_PendingCreations);
			s_PendingCreations.Clear();
			return creations;
		}

		/// <summary>
		/// Flushes pending script additions.
		/// </summary>
		public static NativeList<PendingScriptAddition> FlushScriptAdditions()
		{
			if (!s_Initialized || !s_PendingScripts.IsCreated)
				return new NativeList<PendingScriptAddition>(0, Allocator.Temp);

			var scripts = new NativeList<PendingScriptAddition>(s_PendingScripts.Length, Allocator.Temp);
			scripts.CopyFrom(s_PendingScripts);
			s_PendingScripts.Clear();
			return scripts;
		}

		/// <summary>
		/// Syncs the bridge's entity ID counter with the collection's current state.
		/// Call after external entity creation to prevent ID collisions.
		/// </summary>
		public static void SyncNextEntityId(int nextId)
		{
			var current = s_NextEntityId.Data;
			while (current < nextId)
			{
				var prev = Interlocked.CompareExchange(ref s_NextEntityId.Data, nextId, current);
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
			ref var ctx = ref s_EventContext.Data;
			if (!ctx.IsValid)
				return;

			ctx.PendingEvents.Clear();
			ctx.EventBuffer.Clear();
			ctx.EntitiesToClear.Clear();
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
			ref var ctx = ref s_EventContext.Data;
			if (!ctx.IsValid)
				return;

			ctx.PendingEvents.Add(
				new PendingEventDispatch
				{
					Entity = entity,
					ScriptIndex = scriptIndex,
					ScriptName = scriptName,
					EntityIndex = entityIndex,
					StateRef = stateRef,
					EventStartIndex = eventStartIndex,
					EventCount = eventCount,
				}
			);
		}

		/// <summary>
		/// Adds an event to the event buffer.
		/// </summary>
		public static int AddEvent(LuaEvent evt)
		{
			ref var ctx = ref s_EventContext.Data;
			if (!ctx.IsValid)
				return -1;

			var index = ctx.EventBuffer.Length;
			ctx.EventBuffer.Add(evt);
			return index;
		}

		/// <summary>
		/// Adds an entity to the clear list.
		/// </summary>
		public static void AddEntityToClear(Entity entity)
		{
			ref var ctx = ref s_EventContext.Data;
			if (!ctx.IsValid)
				return;

			ctx.EntitiesToClear.Add(entity);
		}

		/// <summary>
		/// Gets an event from the buffer by index.
		/// </summary>
		public static LuaEvent GetEvent(int index)
		{
			ref var ctx = ref s_EventContext.Data;
			if (!ctx.IsValid || index < 0 || index >= ctx.EventBuffer.Length)
				return default;

			return ctx.EventBuffer[index];
		}
	}
}
