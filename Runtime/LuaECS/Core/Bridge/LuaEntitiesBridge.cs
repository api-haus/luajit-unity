namespace LuaECS.Core
{
	using System.Threading;
	using AOT;
	using Components;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;

	/// <summary>
	/// Bridge functions for entity lifecycle operations.
	/// Lua API: entities.create(), entities.destroy(), entities.add_script(), entities.has_script()
	/// </summary>
	public static partial class LuaECSBridge
	{
		internal static void RegisterEntitiesFunctions(lua_State l)
		{
			Lua.lua_newtable(l);

			RegisterFunction(l, "create", Entities_Create);
			RegisterFunction(l, "destroy", Entities_Destroy);
			RegisterFunction(l, "add_script", Entities_AddScript);
			RegisterFunction(l, "has_script", Entities_HasScript);
			RegisterFunction(l, "remove_component", Entities_RemoveComponent);

			Lua.lua_setglobal(l, "entities");
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Entities_Create(lua_State l)
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

			ref var ctx = ref s_burstContext.Data;
			if (!ctx.isValid)
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			// Allocate new entity ID atomically
			var entityId = Interlocked.Increment(ref s_nextEntityId.Data);

			// Create entity via ECB with all required components
			var entity = ctx.ecb.CreateEntity();
			ctx.ecb.AddComponent(entity, LocalTransform.FromPosition(position));
			ctx.ecb.AddComponent(entity, new LuaEntityId { value = entityId });
			ctx.ecb.AddBuffer<LuaScript>(entity);
			ctx.ecb.AddBuffer<LuaScriptRequest>(entity);
			ctx.ecb.AddBuffer<LuaEvent>(entity);

			// Register in pending map for same-frame script additions
			AddPendingEntity(entityId, entity);

			Lua.lua_pushinteger(l, entityId);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Entities_Destroy(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			if (entityId <= 0)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			ref var ctx = ref s_burstContext.Data;
			if (!ctx.isValid)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			// Try to get entity from registry
			if (!ctx.entityIdMap.TryGetValue(entityId, out var entity) || entity == Entity.Null)
			{
				// Check pending entities
				entity = GetPendingEntity(entityId);
				if (entity == Entity.Null)
				{
					Lua.lua_pushboolean(l, 0);
					return 1;
				}
			}

			// Destroy via ECB - remove LuaEntityId to trigger staged cleanup
			ctx.ecb.RemoveComponent<LuaEntityId>(entity);

			Lua.lua_pushboolean(l, 1);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Entities_AddScript(lua_State l)
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

			ref var ctx = ref s_burstContext.Data;
			if (!ctx.isValid)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			// Try to get entity from registry
			if (!ctx.entityIdMap.TryGetValue(entityId, out var entity) || entity == Entity.Null)
			{
				// Check pending entities
				entity = GetPendingEntity(entityId);
				if (entity == Entity.Null)
				{
					Lua.lua_pushboolean(l, 0);
					return 1;
				}
			}

			// Add script request via ECB
			var request = new LuaScriptRequest
			{
				scriptName = scriptName,
				requestHash = LuaScriptPathUtility.HashScriptName(scriptName.ToString()),
				fulfilled = false,
			};
			ctx.ecb.AppendToBuffer(entity, request);

			Lua.lua_pushboolean(l, 1);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Entities_HasScript(lua_State l)
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
		static int Entities_RemoveComponent(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			if (entityId <= 0)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var componentName = Lua.lua_tostring(l, 2);
			if (string.IsNullOrEmpty(componentName))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			if (!LuaComponentRegistry.TryGetComponentType(componentName, out var componentType))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			ref var ctx = ref s_burstContext.Data;
			if (!ctx.isValid)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			if (!ctx.entityIdMap.TryGetValue(entityId, out var entity) || entity == Entity.Null)
			{
				entity = GetPendingEntity(entityId);
				if (entity == Entity.Null)
				{
					Lua.lua_pushboolean(l, 0);
					return 1;
				}
			}

			ctx.ecb.RemoveComponent(entity, componentType);
			Lua.lua_pushboolean(l, 1);
			return 1;
		}
	}
}
