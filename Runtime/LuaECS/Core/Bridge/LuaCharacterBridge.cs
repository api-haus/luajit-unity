namespace LuaECS.Core
{
	using System;
	using AOT;
	using Components;
	using LuaCharacters;
	using LuaCharacters.Core;
	using LuaNET.LuaJIT;
	using Systems;
	using Unity.Burst;
	using Unity.CharacterController;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics;
	using Unity.Physics;
	using Unity.Transforms;

	public static partial class LuaECSBridge
	{
		public struct CharacterBridgeContext
		{
			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<LuaCharacterControl> characterControlLookup;

			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<KinematicCharacterBody> characterBodyLookup;

			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<PhysicsVelocity> physicsVelocityLookup;

			public bool isValid;
		}

		struct CharacterContextMarker { }

		static readonly SharedStatic<CharacterBridgeContext> s_characterContext =
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
			s_characterContext.Data = new CharacterBridgeContext
			{
				characterControlLookup = controlLookup,
				characterBodyLookup = bodyLookup,
				physicsVelocityLookup = velocityLookup,
				isValid = true,
			};
		}

		internal static void RegisterCharacterFunctions(lua_State l)
		{
			Lua.lua_newtable(l);
			RegisterFunction(l, "create", Character_Create);
			RegisterFunction(l, "set_move_input", Character_SetMoveInput);
			RegisterFunction(l, "set_jump", Character_SetJump);
			RegisterFunction(l, "is_grounded", Character_IsGrounded);
			RegisterFunction(l, "get_velocity", Character_GetVelocity);
			Lua.lua_setglobal(l, "character");
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Character_Create(lua_State l)
		{
			var position = float3.zero;
			var argCount = Lua.lua_gettop(l);

			if (argCount >= 1)
			{
				if (Lua.lua_istable(l, 1) != 0)
				{
					position = TableToFloat3(l, 1);
				}
				else if (argCount >= 3)
				{
					position.x = (float)Lua.lua_tonumber(l, 1);
					position.y = (float)Lua.lua_tonumber(l, 2);
					position.z = (float)Lua.lua_tonumber(l, 3);
				}
			}

			var entityId = CreateCharacterEntity(position);
			if (entityId < 0)
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			Lua.lua_pushinteger(l, entityId);
			return 1;
		}

		static int CreateCharacterEntity(float3 position)
		{
			if (s_world == null)
			{
				Log.Error("[LuaCharacterBridge] World is null");
				return -1;
			}

			var world = s_world;
			var entityManager = world.EntityManager;
			var scriptingSystem = world.GetExistingSystemManaged<LuaScriptingSystem>();
			if (scriptingSystem == null)
			{
				Log.Error("[LuaCharacterBridge] LuaScriptingSystem not found");
				return -1;
			}

			try
			{
				var entity = entityManager.CreateEntity();

				entityManager.AddComponentData(
					entity,
					new LocalTransform
					{
						Position = position,
						Rotation = quaternion.identity,
						Scale = 1f,
					}
				);

				KinematicCharacterUtilities.CreateCharacter(
					entityManager,
					entity,
					AuthoringKinematicCharacterProperties.GetDefault()
				);

				entityManager.AddComponentData(
					entity,
					new LuaCharacterComponent
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
					new LuaCharacterControl { moveInput = float2.zero, jump = false }
				);

				var entityId = AllocateEntityId();
				entityManager.AddComponentData(entity, new LuaEntityId { value = entityId });

				entityManager.AddBuffer<LuaScript>(entity);
				entityManager.AddBuffer<LuaEvent>(entity);

				LuaEntityRegistry.RegisterImmediate(entity, entityId, entityManager);

				Log.Info(
					"[LuaCharacterBridge] Successfully created character entity {0} at position {1}",
					entityId,
					position
				);
				return entityId;
			}
			catch (Exception ex)
			{
				Log.Error("[LuaCharacterBridge] Exception creating character: {0}", ex.Message);
				return -1;
			}
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Character_SetMoveInput(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var x = (float)Lua.lua_tonumber(l, 2);
			var y = (float)Lua.lua_tonumber(l, 3);

			var entity = GetEntityFromIdBurst(entityId);
			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_characterContext.Data;
			if (!ctx.isValid)
				return 0;

			if (!ctx.characterControlLookup.HasComponent(entity))
				return 0;

			var control = ctx.characterControlLookup[entity];
			control.moveInput = new float2(x, y);
			ctx.characterControlLookup[entity] = control;

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Character_SetJump(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var jump = Lua.lua_toboolean(l, 2) != 0;

			var entity = GetEntityFromIdBurst(entityId);
			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_characterContext.Data;
			if (!ctx.isValid)
				return 0;

			if (!ctx.characterControlLookup.HasComponent(entity))
				return 0;

			var control = ctx.characterControlLookup[entity];
			control.jump = jump;
			ctx.characterControlLookup[entity] = control;

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Character_IsGrounded(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			ref var ctx = ref s_characterContext.Data;
			if (!ctx.isValid)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			if (!ctx.characterBodyLookup.HasComponent(entity))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var body = ctx.characterBodyLookup[entity];
			Lua.lua_pushboolean(l, body.IsGrounded ? 1 : 0);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Character_GetVelocity(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			ref var ctx = ref s_characterContext.Data;
			if (!ctx.isValid)
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			if (ctx.characterBodyLookup.HasComponent(entity))
			{
				var body = ctx.characterBodyLookup[entity];
				PushFloat3AsTable(l, body.RelativeVelocity);
				return 1;
			}

			if (ctx.physicsVelocityLookup.HasComponent(entity))
			{
				var velocity = ctx.physicsVelocityLookup[entity];
				PushFloat3AsTable(l, velocity.Linear);
				return 1;
			}

			Lua.lua_pushnil(l);
			return 1;
		}
	}
}
