namespace LuaECS.Core
{
	using AOT;
	using LuaECS.Components;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Entities;
	using Unity.Mathematics;

	public static partial class LuaECSBridge
	{
		internal static void RegisterEntityFunctions(lua_State L)
		{
			RegisterFunction(L, "create_entity", ECS_CreateEntity);
			RegisterFunction(L, "add_script", ECS_AddScript);
			RegisterFunction(L, "has_script", ECS_HasScript);
			RegisterFunction(L, "destroy_entity", ECS_DestroyEntity);
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_CreateEntity(lua_State L)
		{
			var position = float3.zero;
			var argCount = Lua.lua_gettop(L);

			if (argCount >= 1)
			{
				if (Lua.lua_istable(L, 1) != 0)
				{
					position = TableToFloat3Burst(L, 1);
				}
				else if (argCount >= 3)
				{
					position.x = (float)Lua.lua_tonumber(L, 1);
					position.y = (float)Lua.lua_tonumber(L, 2);
					position.z = (float)Lua.lua_tonumber(L, 3);
				}
			}

			var entityId = CreateEntityBurst(position);
			if (entityId < 0)
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			Lua.lua_pushinteger(L, entityId);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_AddScript(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			if (entityId <= 0)
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			if (!TryReadLuaStringBurst(L, 2, out var scriptName))
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			var success = AddScriptBurst(entityId, scriptName);
			Lua.lua_pushboolean(L, success ? 1 : 0);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_HasScript(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			if (entityId <= 0)
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			if (!TryReadLuaStringBurst(L, 2, out var scriptName))
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			var entity = GetEntityFromIdBurst(entityId);
			var hasScript = HasScriptBurst(entity, scriptName);
			Lua.lua_pushboolean(L, hasScript ? 1 : 0);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_DestroyEntity(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			if (entityId <= 0)
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			QueueDestructionBurst(entityId);
			Lua.lua_pushboolean(L, 1);
			return 1;
		}
	}
}
