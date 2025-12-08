namespace LuaECS.Core
{
	using AOT;
	using LuaCharacters;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.CharacterController;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Physics;

	public static partial class LuaECSBridge
	{
		public unsafe struct CharacterBridgeContext
		{
			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<LuaCharacterControl> CharacterControlLookup;

			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<KinematicCharacterBody> CharacterBodyLookup;

			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<PhysicsVelocity> PhysicsVelocityLookup;

			public bool IsValid;
		}

		struct CharacterContextMarker { }

		static readonly SharedStatic<CharacterBridgeContext> s_CharacterContext =
			SharedStatic<CharacterBridgeContext>.GetOrCreate<
				CharacterContextMarker,
				CharacterBridgeContext
			>();

		public static void UpdateCharacterContext(
			ComponentLookup<LuaCharacterControl> controlLookup,
			ComponentLookup<KinematicCharacterBody> bodyLookup,
			ComponentLookup<PhysicsVelocity> velocityLookup
		)
		{
			s_CharacterContext.Data = new CharacterBridgeContext
			{
				CharacterControlLookup = controlLookup,
				CharacterBodyLookup = bodyLookup,
				PhysicsVelocityLookup = velocityLookup,
				IsValid = true,
			};
		}

		internal static void RegisterCharacterFunctions(lua_State L)
		{
			Lua.lua_newtable(L);
			RegisterFunction(L, "create", Character_Create);
			RegisterFunction(L, "set_move_input", Character_SetMoveInput);
			RegisterFunction(L, "set_jump", Character_SetJump);
			RegisterFunction(L, "is_grounded", Character_IsGrounded);
			RegisterFunction(L, "get_velocity", Character_GetVelocity);
			Lua.lua_setglobal(L, "character");
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Character_Create(lua_State L)
		{
			var position = float3.zero;
			var argCount = Lua.lua_gettop(L);

			if (argCount >= 1)
			{
				if (Lua.lua_istable(L, 1) != 0)
				{
					position = TableToFloat3(L, 1);
				}
				else if (argCount >= 3)
				{
					position.x = (float)Lua.lua_tonumber(L, 1);
					position.y = (float)Lua.lua_tonumber(L, 2);
					position.z = (float)Lua.lua_tonumber(L, 3);
				}
			}

			var entityId = CreateCharacterEntity(position);
			if (entityId < 0)
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			Lua.lua_pushinteger(L, entityId);
			return 1;
		}

		static int CreateCharacterEntity(float3 position)
		{
			if (s_World == null)
			{
				Unity.Logging.Log.Error("[LuaCharacterBridge] World is null");
				return -1;
			}

			var world = s_World;
			var entityManager = world.EntityManager;
			var scriptingSystem = world.GetExistingSystemManaged<LuaECS.Systems.LuaScriptingSystem>();
			if (scriptingSystem == null)
			{
				Unity.Logging.Log.Error("[LuaCharacterBridge] LuaScriptingSystem not found");
				return -1;
			}

			try
			{
				var entity = entityManager.CreateEntity();

				entityManager.AddComponentData(
					entity,
					new Unity.Transforms.LocalTransform
					{
						Position = position,
						Rotation = quaternion.identity,
						Scale = 1f,
					}
				);

				Unity.CharacterController.KinematicCharacterUtilities.CreateCharacter(
					entityManager,
					entity,
					Unity.CharacterController.AuthoringKinematicCharacterProperties.GetDefault()
				);

				entityManager.AddComponentData(
					entity,
					new LuaCharacters.LuaCharacterComponent
					{
						rotationSharpness = 25f,
						groundMaxSpeed = 10f,
						groundedMovementSharpness = 15f,
						airAcceleration = 50f,
						airMaxSpeed = 10f,
						airDrag = 0f,
						jumpSpeed = 10f,
						gravity = new float3(0, -30f, 0),
						preventAirAccelerationAgainstUngroundedHits = true,
					}
				);

				entityManager.AddComponentData(
					entity,
					new LuaCharacters.LuaCharacterControl { moveInput = float2.zero, jump = false }
				);

				var entityId = AllocateEntityId();
				entityManager.AddComponentData(
					entity,
					new LuaECS.Components.LuaEntityId { Value = entityId }
				);

				entityManager.AddBuffer<LuaECS.Components.LuaScript>(entity);
				entityManager.AddBuffer<LuaECS.Components.LuaEvent>(entity);
				entityManager.AddBuffer<LuaECS.Components.LuaCommand>(entity);

				scriptingSystem.EntityCollection.RegisterImmediate(entity, entityId);

				Unity.Logging.Log.Info(
					"[LuaCharacterBridge] Successfully created character entity {0} at position {1}",
					entityId,
					position
				);
				return entityId;
			}
			catch (System.Exception ex)
			{
				Unity.Logging.Log.Error(
					"[LuaCharacterBridge] Exception creating character: {0}",
					ex.Message
				);
				return -1;
			}
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Character_SetMoveInput(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			var x = (float)Lua.lua_tonumber(L, 2);
			var y = (float)Lua.lua_tonumber(L, 3);

			var entity = GetEntityFromIdBurst(entityId);
			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_CharacterContext.Data;
			if (!ctx.IsValid)
				return 0;

			if (!ctx.CharacterControlLookup.HasComponent(entity))
				return 0;

			var control = ctx.CharacterControlLookup[entity];
			control.moveInput = new float2(x, y);
			ctx.CharacterControlLookup[entity] = control;

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Character_SetJump(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			var jump = Lua.lua_toboolean(L, 2) != 0;

			var entity = GetEntityFromIdBurst(entityId);
			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_CharacterContext.Data;
			if (!ctx.IsValid)
				return 0;

			if (!ctx.CharacterControlLookup.HasComponent(entity))
				return 0;

			var control = ctx.CharacterControlLookup[entity];
			control.jump = jump;
			ctx.CharacterControlLookup[entity] = control;

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Character_IsGrounded(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			var entity = GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			ref var ctx = ref s_CharacterContext.Data;
			if (!ctx.IsValid)
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			if (!ctx.CharacterBodyLookup.HasComponent(entity))
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			var body = ctx.CharacterBodyLookup[entity];
			Lua.lua_pushboolean(L, body.IsGrounded ? 1 : 0);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Character_GetVelocity(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			var entity = GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			ref var ctx = ref s_CharacterContext.Data;
			if (!ctx.IsValid)
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			if (ctx.CharacterBodyLookup.HasComponent(entity))
			{
				var body = ctx.CharacterBodyLookup[entity];
				PushFloat3AsTable(L, body.RelativeVelocity);
				return 1;
			}
			else if (ctx.PhysicsVelocityLookup.HasComponent(entity))
			{
				var velocity = ctx.PhysicsVelocityLookup[entity];
				PushFloat3AsTable(L, velocity.Linear);
				return 1;
			}

			Lua.lua_pushnil(L);
			return 1;
		}
	}
}
