namespace LuaGame.Bridge
{
	using AOT;
	using Components;
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Entities;
	using Unity.Mathematics;

	/// <summary>
	/// Lua bridge for damage zone functions.
	/// Provides damagezone.create, damagezone.set_damage, etc.
	/// </summary>
	public static class LuaDamageZoneBridge
	{
		public struct DamageZoneBridgeContext
		{
			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<LuaDamageZone> zoneLookup;

			[NativeDisableUnsafePtrRestriction]
			public BufferLookup<LuaDamageZoneHit> hitBufferLookup;

			public EntityCommandBuffer ecb;
			public bool isValid;
		}

		struct DamageZoneContextMarker { }

		static readonly SharedStatic<DamageZoneBridgeContext> s_context =
			SharedStatic<DamageZoneBridgeContext>.GetOrCreate<
				DamageZoneContextMarker,
				DamageZoneBridgeContext
			>();

		public static void UpdateContext(
			ComponentLookup<LuaDamageZone> zoneLookup,
			BufferLookup<LuaDamageZoneHit> hitBufferLookup,
			EntityCommandBuffer ecb
		)
		{
			s_context.Data = new DamageZoneBridgeContext
			{
				zoneLookup = zoneLookup,
				hitBufferLookup = hitBufferLookup,
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

			Lua.lua_pushcfunction(l, DamageZone_CreateSphere);
			Lua.lua_setfield(l, -2, "create");

			Lua.lua_pushcfunction(l, DamageZone_CreateSphere);
			Lua.lua_setfield(l, -2, "create_sphere");

			Lua.lua_pushcfunction(l, DamageZone_CreateBox);
			Lua.lua_setfield(l, -2, "create_box");

			Lua.lua_pushcfunction(l, DamageZone_CreateCapsule);
			Lua.lua_setfield(l, -2, "create_capsule");

			Lua.lua_pushcfunction(l, DamageZone_SetDamage);
			Lua.lua_setfield(l, -2, "set_damage");

			Lua.lua_pushcfunction(l, DamageZone_SetRadius);
			Lua.lua_setfield(l, -2, "set_radius");

			Lua.lua_pushcfunction(l, DamageZone_SetTickInterval);
			Lua.lua_setfield(l, -2, "set_tick_interval");

			Lua.lua_pushcfunction(l, DamageZone_SetTeamMask);
			Lua.lua_setfield(l, -2, "set_team_mask");

			Lua.lua_pushcfunction(l, DamageZone_SetActive);
			Lua.lua_setfield(l, -2, "set_active");

			Lua.lua_pushcfunction(l, DamageZone_GetHits);
			Lua.lua_setfield(l, -2, "get_hits");

			Lua.lua_pushcfunction(l, DamageZone_ClearHits);
			Lua.lua_setfield(l, -2, "clear_hits");

			Lua.lua_pushcfunction(l, DamageZone_Destroy);
			Lua.lua_setfield(l, -2, "destroy");

			Lua.lua_setglobal(l, "damagezone");
		}

		/// <summary>
		/// damagezone.create(x, y, z, radius, damage) -> entityId
		/// Creates a sphere damage zone at position.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int DamageZone_CreateSphere(lua_State l)
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
			var radius = (float)Lua.lua_tonumber(l, 4);
			var damage = (float)Lua.lua_tonumber(l, 5);

			var position = new float3(x, y, z);

			// Create entity with damage zone components
			var entityId = LuaEntityRegistry.Create(position, ctx.ecb);
			var entity = LuaEntityRegistry.GetEntityFromId(entityId);

			if (entity != Entity.Null)
			{
				ctx.ecb.AddComponent(entity, LuaDamageZone.CreateSphere(radius, damage));
				ctx.ecb.AddBuffer<LuaDamageZoneHit>(entity);
				ctx.ecb.AddBuffer<LuaScript>(entity);
			}

			Lua.lua_pushinteger(l, entityId);
			return 1;
		}

		/// <summary>
		/// damagezone.create_box(x, y, z, extX, extY, extZ, damage) -> entityId
		/// Creates a box damage zone at position.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int DamageZone_CreateBox(lua_State l)
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
			var extX = (float)Lua.lua_tonumber(l, 4);
			var extY = (float)Lua.lua_tonumber(l, 5);
			var extZ = (float)Lua.lua_tonumber(l, 6);
			var damage = (float)Lua.lua_tonumber(l, 7);

			var position = new float3(x, y, z);
			var extents = new float3(extX, extY, extZ);

			var entityId = LuaEntityRegistry.Create(position, ctx.ecb);
			var entity = LuaEntityRegistry.GetEntityFromId(entityId);

			if (entity != Entity.Null)
			{
				ctx.ecb.AddComponent(entity, LuaDamageZone.CreateBox(extents, damage));
				ctx.ecb.AddBuffer<LuaDamageZoneHit>(entity);
				ctx.ecb.AddBuffer<LuaScript>(entity);
			}

			Lua.lua_pushinteger(l, entityId);
			return 1;
		}

		/// <summary>
		/// damagezone.create_capsule(x, y, z, radius, halfHeight, damage) -> entityId
		/// Creates a capsule damage zone at position.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int DamageZone_CreateCapsule(lua_State l)
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
			var capsuleRadius = (float)Lua.lua_tonumber(l, 4);
			var halfHeight = (float)Lua.lua_tonumber(l, 5);
			var damage = (float)Lua.lua_tonumber(l, 6);

			var position = new float3(x, y, z);

			var entityId = LuaEntityRegistry.Create(position, ctx.ecb);
			var entity = LuaEntityRegistry.GetEntityFromId(entityId);

			if (entity != Entity.Null)
			{
				ctx.ecb.AddComponent(
					entity,
					LuaDamageZone.CreateCapsule(capsuleRadius, halfHeight, damage)
				);
				ctx.ecb.AddBuffer<LuaDamageZoneHit>(entity);
				ctx.ecb.AddBuffer<LuaScript>(entity);
			}

			Lua.lua_pushinteger(l, entityId);
			return 1;
		}

		/// <summary>
		/// damagezone.set_damage(entityId, damage)
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int DamageZone_SetDamage(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.zoneLookup.HasComponent(entity))
				return 0;

			var damage = (float)Lua.lua_tonumber(l, 2);
			var zone = ctx.zoneLookup[entity];
			zone.damage = damage;
			ctx.zoneLookup[entity] = zone;

			return 0;
		}

		/// <summary>
		/// damagezone.set_radius(entityId, radius)
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int DamageZone_SetRadius(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.zoneLookup.HasComponent(entity))
				return 0;

			var radius = (float)Lua.lua_tonumber(l, 2);
			var zone = ctx.zoneLookup[entity];
			zone.radius = radius;
			ctx.zoneLookup[entity] = zone;

			return 0;
		}

		/// <summary>
		/// damagezone.set_tick_interval(entityId, interval)
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int DamageZone_SetTickInterval(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.zoneLookup.HasComponent(entity))
				return 0;

			var interval = (float)Lua.lua_tonumber(l, 2);
			var zone = ctx.zoneLookup[entity];
			zone.tickInterval = interval;
			zone.hitCooldown = interval;
			ctx.zoneLookup[entity] = zone;

			return 0;
		}

		/// <summary>
		/// damagezone.set_team_mask(entityId, mask)
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int DamageZone_SetTeamMask(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.zoneLookup.HasComponent(entity))
				return 0;

			var mask = (int)Lua.lua_tointeger(l, 2);
			var zone = ctx.zoneLookup[entity];
			zone.teamMask = mask;
			ctx.zoneLookup[entity] = zone;

			return 0;
		}

		/// <summary>
		/// damagezone.set_active(entityId, active)
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int DamageZone_SetActive(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.zoneLookup.HasComponent(entity))
				return 0;

			var active = Lua.lua_toboolean(l, 2) != 0;
			var zone = ctx.zoneLookup[entity];
			zone.isActive = active;
			ctx.zoneLookup[entity] = zone;

			return 0;
		}

		/// <summary>
		/// damagezone.get_hits(entityId) -> {entityId1, entityId2, ...}
		/// Returns array of entity IDs that have been hit.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int DamageZone_GetHits(lua_State l)
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
		/// damagezone.clear_hits(entityId)
		/// Clears the hit buffer.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int DamageZone_ClearHits(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.hitBufferLookup.HasBuffer(entity))
				return 0;

			var hits = ctx.hitBufferLookup[entity];
			hits.Clear();

			return 0;
		}

		/// <summary>
		/// damagezone.destroy(entityId)
		/// Destroys the damage zone entity.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int DamageZone_Destroy(lua_State l)
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
