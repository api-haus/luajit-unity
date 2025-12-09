namespace LuaGame.Bridge
{
	using AOT;
	using LuaGame.Components;
	using LuaECS.Core;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Entities;

	/// <summary>
	/// Lua bridge for time-to-live functions.
	/// Provides ttl.set, ttl.get, ttl.extend, ttl.remove
	/// </summary>
	public static class LuaTTLBridge
	{
		public struct TTLBridgeContext
		{
			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<LuaTimeToLive> ttlLookup;

			public bool isValid;
		}

		struct TTLContextMarker { }

		static readonly SharedStatic<TTLBridgeContext> s_context =
			SharedStatic<TTLBridgeContext>.GetOrCreate<TTLContextMarker, TTLBridgeContext>();

		public static void UpdateContext(ComponentLookup<LuaTimeToLive> ttlLookup)
		{
			s_context.Data = new TTLBridgeContext { ttlLookup = ttlLookup, isValid = true };
		}

		public static void ClearContext()
		{
			s_context.Data = default;
		}

		public static void RegisterFunctions(lua_State l)
		{
			Lua.lua_newtable(l);

			Lua.lua_pushcfunction(l, TTL_Set);
			Lua.lua_setfield(l, -2, "set");

			Lua.lua_pushcfunction(l, TTL_Get);
			Lua.lua_setfield(l, -2, "get");

			Lua.lua_pushcfunction(l, TTL_Extend);
			Lua.lua_setfield(l, -2, "extend");

			Lua.lua_pushcfunction(l, TTL_Remove);
			Lua.lua_setfield(l, -2, "remove");

			Lua.lua_pushcfunction(l, TTL_Reset);
			Lua.lua_setfield(l, -2, "reset");

			Lua.lua_setglobal(l, "ttl");
		}

		/// <summary>
		/// ttl.set(entityId, seconds, destroyOnExpire?)
		/// Sets TTL on entity. destroyOnExpire defaults to true.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int TTL_Set(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid)
				return 0;

			var seconds = (float)Lua.lua_tonumber(l, 2);
			var destroyOnExpire = true;

			if (Lua.lua_gettop(l) >= 3 && Lua.lua_isboolean(l, 3) != 0)
			{
				destroyOnExpire = Lua.lua_toboolean(l, 3) != 0;
			}

			if (ctx.ttlLookup.HasComponent(entity))
			{
				var ttl = ctx.ttlLookup[entity];
				ttl.remaining = seconds;
				ttl.initial = seconds;
				ttl.destroyOnExpire = destroyOnExpire;
				ctx.ttlLookup[entity] = ttl;
			}

			return 0;
		}

		/// <summary>
		/// ttl.get(entityId) -> remaining, initial, progress
		/// Returns remaining time, initial time, and progress (0-1).
		/// Returns nil if entity has no TTL.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int TTL_Get(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.ttlLookup.HasComponent(entity))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			var ttl = ctx.ttlLookup[entity];
			Lua.lua_pushnumber(l, ttl.remaining);
			Lua.lua_pushnumber(l, ttl.initial);
			Lua.lua_pushnumber(l, ttl.Progress);
			return 3;
		}

		/// <summary>
		/// ttl.extend(entityId, seconds)
		/// Adds time to remaining TTL.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int TTL_Extend(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.ttlLookup.HasComponent(entity))
				return 0;

			var seconds = (float)Lua.lua_tonumber(l, 2);
			var ttl = ctx.ttlLookup[entity];
			ttl.Extend(seconds);
			ctx.ttlLookup[entity] = ttl;

			return 0;
		}

		/// <summary>
		/// ttl.remove(entityId)
		/// Removes TTL component from entity (stops countdown).
		/// Note: Cannot remove via bridge, just sets very high remaining time.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int TTL_Remove(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.ttlLookup.HasComponent(entity))
				return 0;

			// Set very high value and disable destroy
			var ttl = ctx.ttlLookup[entity];
			ttl.remaining = float.MaxValue;
			ttl.destroyOnExpire = false;
			ctx.ttlLookup[entity] = ttl;

			return 0;
		}

		/// <summary>
		/// ttl.reset(entityId)
		/// Resets remaining to initial value.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int TTL_Reset(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.ttlLookup.HasComponent(entity))
				return 0;

			var ttl = ctx.ttlLookup[entity];
			ttl.Reset();
			ctx.ttlLookup[entity] = ttl;

			return 0;
		}
	}
}
