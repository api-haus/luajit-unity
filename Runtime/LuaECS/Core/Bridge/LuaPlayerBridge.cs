namespace LuaECS.Core
{
	using AOT;
	using LuaCharacters.ThirdPerson;
	using LuaECS.Components;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;

	public static partial class LuaECSBridge
	{
		internal static void RegisterPlayerFunctions(lua_State L)
		{
			Lua.lua_newtable(L);
			RegisterFunction(L, "get_entity", Player_GetEntity);
			RegisterFunction(L, "get_id", Player_GetId);
			RegisterFunction(L, "get_character", Player_GetCharacter);
			RegisterFunction(L, "set_character", Player_SetCharacter);
			RegisterFunction(L, "attach_script", Player_AttachScript);
			Lua.lua_setglobal(L, "player");
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Player_GetEntity(lua_State L)
		{
			var playerId = (int)Lua.lua_tointeger(L, 1);
			if (!TryGetPlayerEntity(playerId, out var entity, createIfMissing: true))
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			if (!TryEnsureLuaEntityId(entity, out var entityId))
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			Lua.lua_pushinteger(L, entityId);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Player_GetId(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			if (!TryGetPlayerEntityFromLuaId(entityId, out var entity))
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			var tag = s_EntityManager.GetComponentData<LuaPlayerTag>(entity);
			Lua.lua_pushinteger(L, tag.PlayerId);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Player_GetCharacter(lua_State L)
		{
			var playerId = (int)Lua.lua_tointeger(L, 1);
			if (!TryGetPlayerEntity(playerId, out var entity))
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			if (!s_EntityManager.HasComponent<ThirdPersonPlayerControl>(entity))
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			var control = s_EntityManager.GetComponentData<ThirdPersonPlayerControl>(entity);
			if (control.controlledCharacter == Entity.Null)
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			var characterId = s_ScriptingSystem?.GetEntityIdFromEntity(control.controlledCharacter) ?? -1;
			if (characterId <= 0 && !TryEnsureLuaEntityId(control.controlledCharacter, out characterId))
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			Lua.lua_pushinteger(L, characterId);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Player_SetCharacter(lua_State L)
		{
			var playerId = (int)Lua.lua_tointeger(L, 1);
			var characterId = (int)Lua.lua_tointeger(L, 2);

			if (!TryGetPlayerEntity(playerId, out var playerEntity))
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			if (!s_EntityManager.HasComponent<ThirdPersonPlayerControl>(playerEntity))
			{
				Log.Error("[LuaPlayerBridge] Player entity missing ThirdPersonPlayerControl");
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			var characterEntity = Entity.Null;
			if (characterId > 0 && s_ScriptingSystem != null)
				characterEntity = s_ScriptingSystem.GetEntityFromId(characterId);

			if (characterId > 0 && characterEntity == Entity.Null)
			{
				Log.Error("[LuaPlayerBridge] Character entity {0} not found", characterId);
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			var control = s_EntityManager.GetComponentData<ThirdPersonPlayerControl>(playerEntity);
			control.controlledCharacter = characterEntity;
			s_EntityManager.SetComponentData(playerEntity, control);

			Lua.lua_pushboolean(L, 1);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Player_AttachScript(lua_State L)
		{
			var playerId = (int)Lua.lua_tointeger(L, 1);
			if (!TryReadLuaStringBurst(L, 2, out var scriptName))
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			if (!TryGetPlayerEntity(playerId, out var entity))
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			if (!TryEnsureLuaEntityId(entity, out var entityId))
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			var success = AddScriptBurst(entityId, scriptName);
			Lua.lua_pushboolean(L, success ? 1 : 0);
			return 1;
		}

		static bool TryGetPlayerEntity(int playerId, out Entity entity, bool createIfMissing = false)
		{
			entity = Entity.Null;
			if (playerId <= 0 || s_EntityManager == default)
				return false;

			EnsurePlayerQuery();
			if (!s_PlayerQueryInitialized)
				return false;

			using var tags = s_PlayerQuery.ToComponentDataArray<LuaPlayerTag>(Allocator.Temp);
			using var entities = s_PlayerQuery.ToEntityArray(Allocator.Temp);

			for (var i = 0; i < tags.Length; i++)
			{
				if (tags[i].PlayerId == playerId)
				{
					entity = entities[i];
					return entity != Entity.Null && s_EntityManager.Exists(entity);
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
			if (entityId <= 0 || s_ScriptingSystem == null)
				return false;

			entity = s_ScriptingSystem.GetEntityFromId(entityId);
			if (entity == Entity.Null || !s_EntityManager.Exists(entity))
				return false;

			return s_EntityManager.HasComponent<LuaPlayerTag>(entity);
		}

		static bool TryEnsureLuaEntityId(Entity entity, out int entityId)
		{
			entityId = -1;
			if (entity == Entity.Null || s_ScriptingSystem == null || s_EntityManager == default)
				return false;

			var existingId = s_ScriptingSystem.GetEntityIdFromEntity(entity);
			if (existingId > 0)
			{
				entityId = existingId;
				return true;
			}

			if (!s_EntityManager.HasComponent<LuaEntityId>(entity))
			{
				entityId = AllocateEntityId();
				s_EntityManager.AddComponentData(entity, new LuaEntityId { Value = entityId });
			}
			else
			{
				var luaId = s_EntityManager.GetComponentData<LuaEntityId>(entity);
				if (luaId.Value <= 0)
				{
					luaId.Value = AllocateEntityId();
					s_EntityManager.SetComponentData(entity, luaId);
				}
				entityId = luaId.Value;
			}

			s_ScriptingSystem.EntityCollection.RegisterImmediate(entity, entityId);
			return true;
		}

		static void EnsurePlayerQuery()
		{
			if (s_EntityManager == default)
				return;

			if (!s_PlayerQueryInitialized)
			{
				s_PlayerQuery = s_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LuaPlayerTag>());
				s_PlayerQueryInitialized = true;
			}
		}

		static Entity CreatePlayerEntity(int playerId)
		{
			if (s_EntityManager == default)
				return Entity.Null;

			var entity = s_EntityManager.CreateEntity();
			s_EntityManager.AddComponentData(entity, new LuaPlayerTag { PlayerId = playerId });
			s_EntityManager.AddComponentData(
				entity,
				new ThirdPersonPlayerControl
				{
					controlledCharacter = Entity.Null,
					controlledCamera = Entity.Null,
				}
			);
			s_EntityManager.AddComponentData(entity, new ThirdPersonPlayerInputs());

			if (!s_EntityManager.HasBuffer<LuaScriptRequest>(entity))
				s_EntityManager.AddBuffer<LuaScriptRequest>(entity);
			if (!s_EntityManager.HasBuffer<LuaEvent>(entity))
				s_EntityManager.AddBuffer<LuaEvent>(entity);
			if (!s_EntityManager.HasBuffer<LuaCommand>(entity))
				s_EntityManager.AddBuffer<LuaCommand>(entity);

			TryEnsureLuaEntityId(entity, out _);
			return entity;
		}
	}
}
