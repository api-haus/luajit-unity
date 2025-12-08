namespace LuaGame.Bridge
{
	using AOT;
	using LuaECS.Core;
	using LuaGame.Components;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Entities;

	/// <summary>
	/// Lua bridge for health system functions.
	/// Provides health.get, health.set, health.damage, health.is_dead
	/// </summary>
	[BurstCompile]
	public static class LuaHealthBridge
	{
		public struct HealthBridgeContext
		{
			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<LuaHealth> HealthLookup;

			public bool IsValid;
		}

		struct HealthContextMarker { }

		static readonly SharedStatic<HealthBridgeContext> s_Context =
			SharedStatic<HealthBridgeContext>.GetOrCreate<HealthContextMarker, HealthBridgeContext>();

		public static void UpdateContext(ComponentLookup<LuaHealth> healthLookup)
		{
			s_Context.Data = new HealthBridgeContext { HealthLookup = healthLookup, IsValid = true };
		}

		public static void ClearContext()
		{
			s_Context.Data = default;
		}

		public static void RegisterFunctions(lua_State L)
		{
			Lua.lua_newtable(L);

			Lua.lua_pushcfunction(L, Health_Get);
			Lua.lua_setfield(L, -2, "get");

			Lua.lua_pushcfunction(L, Health_Set);
			Lua.lua_setfield(L, -2, "set");

			Lua.lua_pushcfunction(L, Health_Damage);
			Lua.lua_setfield(L, -2, "damage");

			Lua.lua_pushcfunction(L, Health_IsDead);
			Lua.lua_setfield(L, -2, "is_dead");

			Lua.lua_pushcfunction(L, Health_Heal);
			Lua.lua_setfield(L, -2, "heal");

			Lua.lua_setglobal(L, "health");
		}

		/// <summary>
		/// health.get(entity) -> current, max
		/// Returns current and max health, or nil if entity has no health.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Health_Get(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			ref var ctx = ref s_Context.Data;
			if (!ctx.IsValid || !ctx.HealthLookup.HasComponent(entity))
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			var health = ctx.HealthLookup[entity];
			Lua.lua_pushnumber(L, health.Current);
			Lua.lua_pushnumber(L, health.Max);
			return 2;
		}

		/// <summary>
		/// health.set(entity, current, max?)
		/// Sets health. Max is optional, defaults to keeping current max.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Health_Set(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_Context.Data;
			if (!ctx.IsValid || !ctx.HealthLookup.HasComponent(entity))
				return 0;

			var current = (float)Lua.lua_tonumber(L, 2);
			var health = ctx.HealthLookup[entity];

			health.Current = current;

			if (Lua.lua_gettop(L) >= 3 && Lua.lua_isnumber(L, 3) != 0)
			{
				health.Max = (float)Lua.lua_tonumber(L, 3);
			}

			if (health.Current <= 0)
			{
				health.Current = 0;
				health.IsDead = true;
			}
			else
			{
				health.IsDead = false;
			}

			ctx.HealthLookup[entity] = health;
			return 0;
		}

		/// <summary>
		/// health.damage(entity, amount, source?)
		/// Applies damage to entity. Source is optional entity ID.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Health_Damage(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_Context.Data;
			if (!ctx.IsValid || !ctx.HealthLookup.HasComponent(entity))
				return 0;

			var amount = (float)Lua.lua_tonumber(L, 2);
			var health = ctx.HealthLookup[entity];

			health.TakeDamage(amount);
			ctx.HealthLookup[entity] = health;

			return 0;
		}

		/// <summary>
		/// health.is_dead(entity) -> bool
		/// Returns true if entity is dead.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Health_IsDead(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushboolean(L, 1);
				return 1;
			}

			ref var ctx = ref s_Context.Data;
			if (!ctx.IsValid || !ctx.HealthLookup.HasComponent(entity))
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			var health = ctx.HealthLookup[entity];
			Lua.lua_pushboolean(L, health.IsDead ? 1 : 0);
			return 1;
		}

		/// <summary>
		/// health.heal(entity, amount)
		/// Heals entity by amount, clamped to max.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Health_Heal(lua_State L)
		{
			var entityId = (int)Lua.lua_tointeger(L, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_Context.Data;
			if (!ctx.IsValid || !ctx.HealthLookup.HasComponent(entity))
				return 0;

			var amount = (float)Lua.lua_tonumber(L, 2);
			var health = ctx.HealthLookup[entity];

			health.Heal(amount);
			ctx.HealthLookup[entity] = health;

			return 0;
		}
	}
}
