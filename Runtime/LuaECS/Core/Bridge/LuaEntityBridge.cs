namespace LuaECS.Core
{
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Mathematics;

	public static partial class LuaECSBridge
	{
		internal static void RegisterEntityFunctions(lua_State l)
		{
			RegisterFunction(l, "create_entity", ECS_CreateEntity);
			RegisterFunction(l, "add_script", ECS_AddScript);
			RegisterFunction(l, "has_script", ECS_HasScript);
			RegisterFunction(l, "destroy_entity", ECS_DestroyEntity);
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_CreateEntity(lua_State l)
		{
			var position = float3.zero;
			var argCount = Lua.lua_gettop(l);

			if (argCount >= 1)
			{
				if (Lua.lua_istable(l, 1) != 0)
				{
					position = TableToFloat3Burst(l, 1);
				}
				else if (argCount >= 3)
				{
					position.x = (float)Lua.lua_tonumber(l, 1);
					position.y = (float)Lua.lua_tonumber(l, 2);
					position.z = (float)Lua.lua_tonumber(l, 3);
				}
			}

			var entityId = CreateEntityBurst(position);
			if (entityId < 0)
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			Lua.lua_pushinteger(l, entityId);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_AddScript(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			if (entityId <= 0)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			if (!TryReadLuaStringBurst(l, 2, out var scriptName))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var success = AddScriptBurst(entityId, scriptName);
			Lua.lua_pushboolean(l, success ? 1 : 0);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_HasScript(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			if (entityId <= 0)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			if (!TryReadLuaStringBurst(l, 2, out var scriptName))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var entity = GetEntityFromIdBurst(entityId);
			var hasScript = HasScriptBurst(entity, scriptName);
			Lua.lua_pushboolean(l, hasScript ? 1 : 0);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_DestroyEntity(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			if (entityId <= 0)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			QueueDestructionBurst(entityId);
			Lua.lua_pushboolean(l, 1);
			return 1;
		}
	}
}
