namespace LuaGame.Bridge
{
	using AOT;
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaGame.Components;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;

	/// <summary>
	/// Lua bridge for projectile functions.
	/// Provides projectile.create, projectile.set_velocity, etc.
	/// </summary>
	public static class LuaProjectileBridge
	{
		public struct ProjectileBridgeContext
		{
			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<LuaProjectile> projectileLookup;

			[NativeDisableUnsafePtrRestriction]
			public BufferLookup<LuaProjectileHit> hitBufferLookup;

			public EntityCommandBuffer ecb;
			public bool isValid;
		}

		struct ProjectileContextMarker { }

		static readonly SharedStatic<ProjectileBridgeContext> s_context =
			SharedStatic<ProjectileBridgeContext>.GetOrCreate<
				ProjectileContextMarker,
				ProjectileBridgeContext
			>();

		static EntityManager s_entityManager;

		public static void UpdateContext(
			ComponentLookup<LuaProjectile> projectileLookup,
			BufferLookup<LuaProjectileHit> hitBufferLookup,
			EntityCommandBuffer ecb,
			EntityManager entityManager
		)
		{
			s_context.Data = new ProjectileBridgeContext
			{
				projectileLookup = projectileLookup,
				hitBufferLookup = hitBufferLookup,
				ecb = ecb,
				isValid = true,
			};
			s_entityManager = entityManager;
		}

		public static void ClearContext()
		{
			s_context.Data = default;
		}

		public static void RegisterFunctions(lua_State l)
		{
			Lua.lua_newtable(l);

			Lua.lua_pushcfunction(l, Projectile_Create);
			Lua.lua_setfield(l, -2, "create");

			Lua.lua_pushcfunction(l, Projectile_CreatePiercing);
			Lua.lua_setfield(l, -2, "create_piercing");

			Lua.lua_pushcfunction(l, Projectile_CreateBouncing);
			Lua.lua_setfield(l, -2, "create_bouncing");

			Lua.lua_pushcfunction(l, Projectile_SetVelocity);
			Lua.lua_setfield(l, -2, "set_velocity");

			Lua.lua_pushcfunction(l, Projectile_SetSpeed);
			Lua.lua_setfield(l, -2, "set_speed");

			Lua.lua_pushcfunction(l, Projectile_SetDamage);
			Lua.lua_setfield(l, -2, "set_damage");

			Lua.lua_pushcfunction(l, Projectile_SetPierce);
			Lua.lua_setfield(l, -2, "set_pierce");

			Lua.lua_pushcfunction(l, Projectile_SetBounce);
			Lua.lua_setfield(l, -2, "set_bounce");

			Lua.lua_pushcfunction(l, Projectile_SetSource);
			Lua.lua_setfield(l, -2, "set_source");

			Lua.lua_pushcfunction(l, Projectile_SetTeam);
			Lua.lua_setfield(l, -2, "set_team");

			Lua.lua_pushcfunction(l, Projectile_SetHitCooldown);
			Lua.lua_setfield(l, -2, "set_hit_cooldown");

			Lua.lua_pushcfunction(l, Projectile_SetActive);
			Lua.lua_setfield(l, -2, "set_active");

			Lua.lua_pushcfunction(l, Projectile_GetHits);
			Lua.lua_setfield(l, -2, "get_hits");

			Lua.lua_pushcfunction(l, Projectile_GetVelocity);
			Lua.lua_setfield(l, -2, "get_velocity");

			Lua.lua_pushcfunction(l, Projectile_Destroy);
			Lua.lua_setfield(l, -2, "destroy");

			Lua.lua_setglobal(l, "projectile");
		}

		/// <summary>
		/// projectile.create(x, y, z, dirX, dirY, dirZ, speed, damage) -> entityId
		/// Creates a simple projectile at position with direction.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Projectile_Create(lua_State l)
		{
			ref var ctx = ref s_context.Data;
			if (!ctx.isValid)
			{
				Lua.lua_pushinteger(l, -1);
				return 1;
			}

			var x = (float)Lua.lua_tonumber(l, 1);
			var y = (float)Lua.lua_tonumber(l, 2);
			var z = (float)Lua.lua_tonumber(l, 3);
			var dirX = (float)Lua.lua_tonumber(l, 4);
			var dirY = (float)Lua.lua_tonumber(l, 5);
			var dirZ = (float)Lua.lua_tonumber(l, 6);
			var speed = (float)Lua.lua_tonumber(l, 7);
			var damage = (float)Lua.lua_tonumber(l, 8);

			var position = new float3(x, y, z);
			var direction = new float3(dirX, dirY, dirZ);

			// Create entity with projectile components
			var entityId = LuaEntityRegistry.Create(position, ctx.ecb);
			var entity = LuaEntityRegistry.GetEntityFromId(entityId);

			if (entity != Entity.Null)
			{
				var proj = LuaProjectile.Create(direction, speed, damage);
				proj.previousPosition = position;

				ctx.ecb.AddComponent(entity, proj);
				ctx.ecb.AddBuffer<LuaProjectileHit>(entity);
				ctx.ecb.AddBuffer<LuaScript>(entity);
			}

			Lua.lua_pushinteger(l, entityId);
			return 1;
		}

		/// <summary>
		/// projectile.create_piercing(x, y, z, dirX, dirY, dirZ, speed, damage, pierceCount) -> entityId
		/// Creates a projectile that can pierce through multiple targets.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Projectile_CreatePiercing(lua_State l)
		{
			ref var ctx = ref s_context.Data;
			if (!ctx.isValid)
			{
				Lua.lua_pushinteger(l, -1);
				return 1;
			}

			var x = (float)Lua.lua_tonumber(l, 1);
			var y = (float)Lua.lua_tonumber(l, 2);
			var z = (float)Lua.lua_tonumber(l, 3);
			var dirX = (float)Lua.lua_tonumber(l, 4);
			var dirY = (float)Lua.lua_tonumber(l, 5);
			var dirZ = (float)Lua.lua_tonumber(l, 6);
			var speed = (float)Lua.lua_tonumber(l, 7);
			var damage = (float)Lua.lua_tonumber(l, 8);
			var pierceCount = (int)Lua.lua_tointeger(l, 9);

			var position = new float3(x, y, z);
			var direction = new float3(dirX, dirY, dirZ);

			var entityId = LuaEntityRegistry.Create(position, ctx.ecb);
			var entity = LuaEntityRegistry.GetEntityFromId(entityId);

			if (entity != Entity.Null)
			{
				var proj = LuaProjectile.CreatePiercing(direction, speed, damage, pierceCount);
				proj.previousPosition = position;

				ctx.ecb.AddComponent(entity, proj);
				ctx.ecb.AddBuffer<LuaProjectileHit>(entity);
				ctx.ecb.AddBuffer<LuaScript>(entity);
			}

			Lua.lua_pushinteger(l, entityId);
			return 1;
		}

		/// <summary>
		/// projectile.create_bouncing(x, y, z, dirX, dirY, dirZ, speed, damage, bounceCount) -> entityId
		/// Creates a projectile that bounces off surfaces.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Projectile_CreateBouncing(lua_State l)
		{
			ref var ctx = ref s_context.Data;
			if (!ctx.isValid)
			{
				Lua.lua_pushinteger(l, -1);
				return 1;
			}

			var x = (float)Lua.lua_tonumber(l, 1);
			var y = (float)Lua.lua_tonumber(l, 2);
			var z = (float)Lua.lua_tonumber(l, 3);
			var dirX = (float)Lua.lua_tonumber(l, 4);
			var dirY = (float)Lua.lua_tonumber(l, 5);
			var dirZ = (float)Lua.lua_tonumber(l, 6);
			var speed = (float)Lua.lua_tonumber(l, 7);
			var damage = (float)Lua.lua_tonumber(l, 8);
			var bounceCount = (int)Lua.lua_tointeger(l, 9);

			var position = new float3(x, y, z);
			var direction = new float3(dirX, dirY, dirZ);

			var entityId = LuaEntityRegistry.Create(position, ctx.ecb);
			var entity = LuaEntityRegistry.GetEntityFromId(entityId);

			if (entity != Entity.Null)
			{
				var proj = LuaProjectile.CreateBouncing(direction, speed, damage, bounceCount);
				proj.previousPosition = position;

				ctx.ecb.AddComponent(entity, proj);
				ctx.ecb.AddBuffer<LuaProjectileHit>(entity);
				ctx.ecb.AddBuffer<LuaScript>(entity);
			}

			Lua.lua_pushinteger(l, entityId);
			return 1;
		}

		/// <summary>
		/// projectile.set_velocity(entityId, vx, vy, vz)
		/// Sets projectile velocity directly.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Projectile_SetVelocity(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.projectileLookup.HasComponent(entity))
				return 0;

			var vx = (float)Lua.lua_tonumber(l, 2);
			var vy = (float)Lua.lua_tonumber(l, 3);
			var vz = (float)Lua.lua_tonumber(l, 4);

			var proj = ctx.projectileLookup[entity];
			proj.velocity = new float3(vx, vy, vz);
			proj.speed = math.length(proj.velocity);
			ctx.projectileLookup[entity] = proj;

			return 0;
		}

		/// <summary>
		/// projectile.set_speed(entityId, speed)
		/// Sets speed while maintaining direction.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Projectile_SetSpeed(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.projectileLookup.HasComponent(entity))
				return 0;

			var speed = (float)Lua.lua_tonumber(l, 2);
			var proj = ctx.projectileLookup[entity];
			proj.SetSpeed(speed);
			ctx.projectileLookup[entity] = proj;

			return 0;
		}

		/// <summary>
		/// projectile.set_damage(entityId, damage)
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Projectile_SetDamage(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.projectileLookup.HasComponent(entity))
				return 0;

			var damage = (float)Lua.lua_tonumber(l, 2);
			var proj = ctx.projectileLookup[entity];
			proj.damage = damage;
			ctx.projectileLookup[entity] = proj;

			return 0;
		}

		/// <summary>
		/// projectile.set_pierce(entityId, count)
		/// Sets pierce count (-1 for infinite).
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Projectile_SetPierce(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.projectileLookup.HasComponent(entity))
				return 0;

			var count = (int)Lua.lua_tointeger(l, 2);
			var proj = ctx.projectileLookup[entity];
			proj.pierceCount = count;
			proj.maxPierces = count;
			ctx.projectileLookup[entity] = proj;

			return 0;
		}

		/// <summary>
		/// projectile.set_bounce(entityId, count)
		/// Sets bounce count (-1 for infinite).
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Projectile_SetBounce(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.projectileLookup.HasComponent(entity))
				return 0;

			var count = (int)Lua.lua_tointeger(l, 2);
			var proj = ctx.projectileLookup[entity];
			proj.bounceCount = count;
			proj.maxBounces = count;
			ctx.projectileLookup[entity] = proj;

			return 0;
		}

		/// <summary>
		/// projectile.set_source(entityId, sourceEntityId)
		/// Sets source entity (avoid self-damage).
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Projectile_SetSource(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.projectileLookup.HasComponent(entity))
				return 0;

			var sourceId = (int)Lua.lua_tointeger(l, 2);
			var proj = ctx.projectileLookup[entity];
			proj.sourceEntityId = sourceId;
			ctx.projectileLookup[entity] = proj;

			return 0;
		}

		/// <summary>
		/// projectile.set_team(entityId, teamId)
		/// Sets team for damage filtering.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Projectile_SetTeam(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.projectileLookup.HasComponent(entity))
				return 0;

			var teamId = (int)Lua.lua_tointeger(l, 2);
			var proj = ctx.projectileLookup[entity];
			proj.teamId = teamId;
			ctx.projectileLookup[entity] = proj;

			return 0;
		}

		/// <summary>
		/// projectile.set_hit_cooldown(entityId, seconds)
		/// Sets cooldown before hitting same target again.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Projectile_SetHitCooldown(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.projectileLookup.HasComponent(entity))
				return 0;

			var cooldown = (float)Lua.lua_tonumber(l, 2);
			var proj = ctx.projectileLookup[entity];
			proj.hitCooldown = cooldown;
			ctx.projectileLookup[entity] = proj;

			return 0;
		}

		/// <summary>
		/// projectile.set_active(entityId, active)
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Projectile_SetActive(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.projectileLookup.HasComponent(entity))
				return 0;

			var active = Lua.lua_toboolean(l, 2) != 0;
			var proj = ctx.projectileLookup[entity];
			proj.isActive = active;
			ctx.projectileLookup[entity] = proj;

			return 0;
		}

		/// <summary>
		/// projectile.get_hits(entityId) -> {entityId1, entityId2, ...}
		/// Returns array of entity IDs hit by this projectile.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Projectile_GetHits(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_newtable(l);
				return 1;
			}

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.hitBufferLookup.HasBuffer(entity))
			{
				Lua.lua_newtable(l);
				return 1;
			}

			var hits = ctx.hitBufferLookup[entity];

			Lua.lua_newtable(l);
			for (var i = 0; i < hits.Length; i++)
			{
				Lua.lua_pushinteger(l, hits[i].entityId);
				Lua.lua_rawseti(l, -2, i + 1);
			}

			return 1;
		}

		/// <summary>
		/// projectile.get_velocity(entityId) -> vx, vy, vz
		/// Returns current velocity components.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Projectile_GetVelocity(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushnumber(l, 0);
				Lua.lua_pushnumber(l, 0);
				Lua.lua_pushnumber(l, 0);
				return 3;
			}

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.projectileLookup.HasComponent(entity))
			{
				Lua.lua_pushnumber(l, 0);
				Lua.lua_pushnumber(l, 0);
				Lua.lua_pushnumber(l, 0);
				return 3;
			}

			var proj = ctx.projectileLookup[entity];
			Lua.lua_pushnumber(l, proj.velocity.x);
			Lua.lua_pushnumber(l, proj.velocity.y);
			Lua.lua_pushnumber(l, proj.velocity.z);
			return 3;
		}

		/// <summary>
		/// projectile.destroy(entityId)
		/// Destroys the projectile entity.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Projectile_Destroy(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid)
				return 0;

			LuaEntityRegistry.Destroy(entityId, ctx.ecb);

			return 0;
		}
	}
}
