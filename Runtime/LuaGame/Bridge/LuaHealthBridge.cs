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
	public static class LuaHealthBridge
	{
		public struct HealthBridgeContext
		{
			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<LuaHealth> healthLookup;

			public EntityCommandBuffer ecb;
			public bool isValid;
		}

		struct HealthContextMarker { }

		static readonly SharedStatic<HealthBridgeContext> s_context =
			SharedStatic<HealthBridgeContext>.GetOrCreate<HealthContextMarker, HealthBridgeContext>();

		public static void UpdateContext(ComponentLookup<LuaHealth> healthLookup, EntityCommandBuffer ecb)
		{
			s_context.Data = new HealthBridgeContext
			{
				healthLookup = healthLookup,
				ecb = ecb,
				isValid = true,
			};
		}

		public static void ClearContext()
		{
			s_context.Data = default;
		}

		public static void RegisterFunctions(lua_State l)
		{
			Lua.lua_newtable(l);

			Lua.lua_pushcfunction(l, Health_Add);
			Lua.lua_setfield(l, -2, "add");

			Lua.lua_pushcfunction(l, Health_Get);
			Lua.lua_setfield(l, -2, "get");

			Lua.lua_pushcfunction(l, Health_Set);
			Lua.lua_setfield(l, -2, "set");

			Lua.lua_pushcfunction(l, Health_Damage);
			Lua.lua_setfield(l, -2, "damage");

			Lua.lua_pushcfunction(l, Health_IsDead);
			Lua.lua_setfield(l, -2, "is_dead");

			Lua.lua_pushcfunction(l, Health_Heal);
			Lua.lua_setfield(l, -2, "heal");

			Lua.lua_setglobal(l, "health");
		}

		/// <summary>
		/// health.add(entityId, maxHealth)
		/// Adds health component and damageable tag to entity.
		/// Makes the entity participate in damage zone interactions.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Health_Add(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			// Don't add if already has health
			if (ctx.healthLookup.HasComponent(entity))
			{
				Lua.lua_pushboolean(l, 1);
				return 1;
			}

			var maxHealth = (float)Lua.lua_tonumber(l, 2);
			if (maxHealth <= 0)
				maxHealth = 100f;

			// Add health component and damageable tag
			ctx.ecb.AddComponent(entity, LuaHealth.Create(maxHealth));
			ctx.ecb.AddComponent(entity, new LuaDamageableTag());

			Lua.lua_pushboolean(l, 1);
			return 1;
		}

		/// <summary>
		/// health.get(entity) -> current, max
		/// Returns current and max health, or nil if entity has no health.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Health_Get(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.healthLookup.HasComponent(entity))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			var health = ctx.healthLookup[entity];
			Lua.lua_pushnumber(l, health.current);
			Lua.lua_pushnumber(l, health.max);
			return 2;
		}

		/// <summary>
		/// health.set(entity, current, max?)
		/// Sets health. Max is optional, defaults to keeping current max.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Health_Set(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.healthLookup.HasComponent(entity))
				return 0;

			var current = (float)Lua.lua_tonumber(l, 2);
			var health = ctx.healthLookup[entity];

			health.current = current;

			if (Lua.lua_gettop(l) >= 3 && Lua.lua_isnumber(l, 3) != 0)
			{
				health.max = (float)Lua.lua_tonumber(l, 3);
			}

			if (health.current <= 0)
			{
				health.current = 0;
				health.isDead = true;
			}
			else
			{
				health.isDead = false;
			}

			ctx.healthLookup[entity] = health;
			return 0;
		}

		/// <summary>
		/// health.damage(entity, amount, source?)
		/// Applies damage to entity. Source is optional entity ID.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Health_Damage(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.healthLookup.HasComponent(entity))
				return 0;

			var amount = (float)Lua.lua_tonumber(l, 2);
			var health = ctx.healthLookup[entity];

			health.TakeDamage(amount);
			ctx.healthLookup[entity] = health;

			return 0;
		}

		/// <summary>
		/// health.is_dead(entity) -> bool
		/// Returns true if entity is dead.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Health_IsDead(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushboolean(l, 1);
				return 1;
			}

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.healthLookup.HasComponent(entity))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var health = ctx.healthLookup[entity];
			Lua.lua_pushboolean(l, health.isDead ? 1 : 0);
			return 1;
		}

		/// <summary>
		/// health.heal(entity, amount)
		/// Heals entity by amount, clamped to max.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Health_Heal(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.healthLookup.HasComponent(entity))
				return 0;

			var amount = (float)Lua.lua_tonumber(l, 2);
			var health = ctx.healthLookup[entity];

			health.Heal(amount);
			ctx.healthLookup[entity] = health;

			return 0;
		}
	}
}
