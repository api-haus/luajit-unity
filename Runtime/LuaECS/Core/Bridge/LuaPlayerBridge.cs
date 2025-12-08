namespace LuaECS.Core
{
	using AOT;
	using Components;
	using LuaCharacters.ThirdPerson;
	using LuaCharacters.ThirdPerson.Components;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;

	public static partial class LuaECSBridge
	{
		internal static void RegisterPlayerFunctions(lua_State l)
		{
			Lua.lua_newtable(l);
			RegisterFunction(l, "get_entity", Player_GetEntity);
			RegisterFunction(l, "get_id", Player_GetId);
			RegisterFunction(l, "get_character", Player_GetCharacter);
			RegisterFunction(l, "set_character", Player_SetCharacter);
			RegisterFunction(l, "attach_script", Player_AttachScript);
			Lua.lua_setglobal(l, "player");
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Player_GetEntity(lua_State l)
		{
			var playerId = (int)Lua.lua_tointeger(l, 1);
			if (!TryGetPlayerEntity(playerId, out var entity, createIfMissing: true))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			if (!TryEnsureLuaEntityId(entity, out var entityId))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			Lua.lua_pushinteger(l, entityId);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Player_GetId(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			if (!TryGetPlayerEntityFromLuaId(entityId, out var entity))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			var tag = s_entityManager.GetComponentData<LuaPlayerTag>(entity);
			Lua.lua_pushinteger(l, tag.playerId);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Player_GetCharacter(lua_State l)
		{
			var playerId = (int)Lua.lua_tointeger(l, 1);
			if (!TryGetPlayerEntity(playerId, out var entity))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			if (!s_entityManager.HasComponent<ThirdPersonPlayerControl>(entity))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			var control = s_entityManager.GetComponentData<ThirdPersonPlayerControl>(entity);
			if (control.controlledCharacter == Entity.Null)
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			var characterId = LuaEntityRegistry.GetEntityIdFromEntity(
				control.controlledCharacter,
				s_entityManager
			);
			if (characterId <= 0 && !TryEnsureLuaEntityId(control.controlledCharacter, out characterId))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			Lua.lua_pushinteger(l, characterId);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Player_SetCharacter(lua_State l)
		{
			var playerId = (int)Lua.lua_tointeger(l, 1);
			var characterId = (int)Lua.lua_tointeger(l, 2);

			if (!TryGetPlayerEntity(playerId, out var playerEntity))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			if (!s_entityManager.HasComponent<ThirdPersonPlayerControl>(playerEntity))
			{
				Log.Error("[LuaPlayerBridge] Player entity missing ThirdPersonPlayerControl");
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var characterEntity = Entity.Null;
			if (characterId > 0)
				characterEntity = LuaEntityRegistry.GetEntityFromId(characterId);

			if (characterId > 0 && characterEntity == Entity.Null)
			{
				Log.Error("[LuaPlayerBridge] Character entity {0} not found", characterId);
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var control = s_entityManager.GetComponentData<ThirdPersonPlayerControl>(playerEntity);
			control.controlledCharacter = characterEntity;
			s_entityManager.SetComponentData(playerEntity, control);

			Lua.lua_pushboolean(l, 1);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Player_AttachScript(lua_State l)
		{
			var playerId = (int)Lua.lua_tointeger(l, 1);
			if (!TryReadLuaStringBurst(l, 2, out var scriptName))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			if (!TryGetPlayerEntity(playerId, out var entity))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			if (!TryEnsureLuaEntityId(entity, out var entityId))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var success = AddScriptBurst(entityId, scriptName);
			Lua.lua_pushboolean(l, success ? 1 : 0);
			return 1;
		}

		static bool TryGetPlayerEntity(int playerId, out Entity entity, bool createIfMissing = false)
		{
			entity = Entity.Null;
			if (playerId <= 0 || s_entityManager == default)
				return false;

			EnsurePlayerQuery();
			if (!s_playerQueryInitialized)
				return false;

			using var tags = s_playerQuery.ToComponentDataArray<LuaPlayerTag>(Allocator.Temp);
			using var entities = s_playerQuery.ToEntityArray(Allocator.Temp);

			for (var i = 0; i < tags.Length; i++)
			{
				if (tags[i].playerId == playerId)
				{
					entity = entities[i];
					return entity != Entity.Null && s_entityManager.Exists(entity);
				}
			}

			if (!createIfMissing)
				return false;

			entity = CreatePlayerEntity(playerId);
			return entity != Entity.Null;
		}

		static bool TryGetPlayerEntityFromLuaId(int entityId, out Entity entity)
		{
			entity = Entity.Null;
			if (entityId <= 0 || s_entityManager == default)
				return false;

			entity = LuaEntityRegistry.GetEntityFromId(entityId);
			if (entity == Entity.Null || !s_entityManager.Exists(entity))
				return false;

			return s_entityManager.HasComponent<LuaPlayerTag>(entity);
		}

		static bool TryEnsureLuaEntityId(Entity entity, out int entityId)
		{
			entityId = -1;
			if (entity == Entity.Null || s_entityManager == default)
				return false;

			var existingId = LuaEntityRegistry.GetEntityIdFromEntity(entity, s_entityManager);
			if (existingId > 0)
			{
				entityId = existingId;
				return true;
			}

			if (!s_entityManager.HasComponent<LuaEntityId>(entity))
			{
				entityId = AllocateEntityId();
				s_entityManager.AddComponentData(entity, new LuaEntityId { value = entityId });
			}
			else
			{
				var luaId = s_entityManager.GetComponentData<LuaEntityId>(entity);
				if (luaId.value <= 0)
				{
					luaId.value = AllocateEntityId();
					s_entityManager.SetComponentData(entity, luaId);
				}
				entityId = luaId.value;
			}

			LuaEntityRegistry.RegisterImmediate(entity, entityId, s_entityManager);
			return true;
		}

		static void EnsurePlayerQuery()
		{
			if (s_entityManager == default)
				return;

			if (!s_playerQueryInitialized)
			{
				s_playerQuery = s_entityManager.CreateEntityQuery(ComponentType.ReadOnly<LuaPlayerTag>());
				s_playerQueryInitialized = true;
			}
		}

		static Entity CreatePlayerEntity(int playerId)
		{
			if (s_entityManager == default)
				return Entity.Null;

			var entity = s_entityManager.CreateEntity();
			s_entityManager.AddComponentData(entity, new LuaPlayerTag { playerId = playerId });
			s_entityManager.AddComponentData(
				entity,
				new ThirdPersonPlayerControl
				{
					controlledCharacter = Entity.Null,
					controlledCamera = Entity.Null,
				}
			);
			s_entityManager.AddComponentData(entity, new ThirdPersonPlayerInputs());

			if (!s_entityManager.HasBuffer<LuaScriptRequest>(entity))
				s_entityManager.AddBuffer<LuaScriptRequest>(entity);
			if (!s_entityManager.HasBuffer<LuaEvent>(entity))
				s_entityManager.AddBuffer<LuaEvent>(entity);
			if (!s_entityManager.HasBuffer<LuaCommand>(entity))
				s_entityManager.AddBuffer<LuaCommand>(entity);

			TryEnsureLuaEntityId(entity, out _);
			return entity;
		}
	}
}
