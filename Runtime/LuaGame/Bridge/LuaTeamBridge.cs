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
	/// Lua bridge for team/faction functions.
	/// Provides team.set, team.get, team.is_enemy, team.is_ally, etc.
	/// </summary>
	public static class LuaTeamBridge
	{
		public struct TeamBridgeContext
		{
			[NativeDisableUnsafePtrRestriction]
			public ComponentLookup<LuaTeam> teamLookup;

			public bool isValid;
		}

		struct TeamContextMarker { }

		static readonly SharedStatic<TeamBridgeContext> s_context =
			SharedStatic<TeamBridgeContext>.GetOrCreate<TeamContextMarker, TeamBridgeContext>();

		public static void UpdateContext(ComponentLookup<LuaTeam> teamLookup)
		{
			s_context.Data = new TeamBridgeContext { teamLookup = teamLookup, isValid = true };
		}

		public static void ClearContext()
		{
			s_context.Data = default;
		}

		public static void RegisterFunctions(lua_State l)
		{
			Lua.lua_newtable(l);

			Lua.lua_pushcfunction(l, Team_Set);
			Lua.lua_setfield(l, -2, "set");

			Lua.lua_pushcfunction(l, Team_Get);
			Lua.lua_setfield(l, -2, "get");

			Lua.lua_pushcfunction(l, Team_IsEnemy);
			Lua.lua_setfield(l, -2, "is_enemy");

			Lua.lua_pushcfunction(l, Team_IsAlly);
			Lua.lua_setfield(l, -2, "is_ally");

			Lua.lua_pushcfunction(l, Team_CanDamage);
			Lua.lua_setfield(l, -2, "can_damage");

			Lua.lua_pushcfunction(l, Team_SetAllyMask);
			Lua.lua_setfield(l, -2, "set_ally_mask");

			Lua.lua_pushcfunction(l, Team_SetEnemyMask);
			Lua.lua_setfield(l, -2, "set_enemy_mask");

			Lua.lua_pushcfunction(l, Team_AddAlly);
			Lua.lua_setfield(l, -2, "add_ally");

			Lua.lua_pushcfunction(l, Team_AddEnemy);
			Lua.lua_setfield(l, -2, "add_enemy");

			Lua.lua_setglobal(l, "team");
		}

		/// <summary>
		/// team.set(entityId, teamId)
		/// Sets entity's team (0-31).
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Team_Set(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.teamLookup.HasComponent(entity))
				return 0;

			var teamId = (int)Lua.lua_tointeger(l, 2);
			var team = ctx.teamLookup[entity];

			// Update team ID and recalculate default masks
			var oldBit = team.TeamBit;
			team.teamId = teamId;
			var newBit = team.TeamBit;

			// Update ally mask: remove old team bit, add new team bit
			team.allyMask = (team.allyMask & ~oldBit) | newBit;

			// Update enemy mask: add old team bit, remove new team bit
			team.enemyMask = (team.enemyMask | oldBit) & ~newBit;

			ctx.teamLookup[entity] = team;
			return 0;
		}

		/// <summary>
		/// team.get(entityId) -> teamId, allyMask, enemyMask
		/// Returns team info or nil if no team component.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Team_Get(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.teamLookup.HasComponent(entity))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			var team = ctx.teamLookup[entity];
			Lua.lua_pushinteger(l, team.teamId);
			Lua.lua_pushinteger(l, team.allyMask);
			Lua.lua_pushinteger(l, team.enemyMask);
			return 3;
		}

		/// <summary>
		/// team.is_enemy(entityA, entityB) -> bool
		/// Returns true if entityA considers entityB an enemy.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Team_IsEnemy(lua_State l)
		{
			var entityIdA = (int)Lua.lua_tointeger(l, 1);
			var entityIdB = (int)Lua.lua_tointeger(l, 2);

			var entityA = LuaECSBridge.GetEntityFromIdBurst(entityIdA);
			var entityB = LuaECSBridge.GetEntityFromIdBurst(entityIdB);

			if (entityA == Entity.Null || entityB == Entity.Null)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.teamLookup.HasComponent(entityA) || !ctx.teamLookup.HasComponent(entityB))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var teamA = ctx.teamLookup[entityA];
			var teamB = ctx.teamLookup[entityB];

			Lua.lua_pushboolean(l, teamA.IsEnemy(teamB) ? 1 : 0);
			return 1;
		}

		/// <summary>
		/// team.is_ally(entityA, entityB) -> bool
		/// Returns true if entityA considers entityB an ally.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Team_IsAlly(lua_State l)
		{
			var entityIdA = (int)Lua.lua_tointeger(l, 1);
			var entityIdB = (int)Lua.lua_tointeger(l, 2);

			var entityA = LuaECSBridge.GetEntityFromIdBurst(entityIdA);
			var entityB = LuaECSBridge.GetEntityFromIdBurst(entityIdB);

			if (entityA == Entity.Null || entityB == Entity.Null)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.teamLookup.HasComponent(entityA) || !ctx.teamLookup.HasComponent(entityB))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var teamA = ctx.teamLookup[entityA];
			var teamB = ctx.teamLookup[entityB];

			Lua.lua_pushboolean(l, teamA.IsAlly(teamB) ? 1 : 0);
			return 1;
		}

		/// <summary>
		/// team.can_damage(entityA, entityB) -> bool
		/// Returns true if entityA can damage entityB (not an ally).
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Team_CanDamage(lua_State l)
		{
			var entityIdA = (int)Lua.lua_tointeger(l, 1);
			var entityIdB = (int)Lua.lua_tointeger(l, 2);

			var entityA = LuaECSBridge.GetEntityFromIdBurst(entityIdA);
			var entityB = LuaECSBridge.GetEntityFromIdBurst(entityIdB);

			if (entityA == Entity.Null || entityB == Entity.Null)
			{
				Lua.lua_pushboolean(l, 1); // Unknown entities can be damaged
				return 1;
			}

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid)
			{
				Lua.lua_pushboolean(l, 1);
				return 1;
			}

			// If either lacks team component, allow damage
			if (!ctx.teamLookup.HasComponent(entityA) || !ctx.teamLookup.HasComponent(entityB))
			{
				Lua.lua_pushboolean(l, 1);
				return 1;
			}

			var teamA = ctx.teamLookup[entityA];
			var teamB = ctx.teamLookup[entityB];

			Lua.lua_pushboolean(l, teamA.CanDamage(teamB) ? 1 : 0);
			return 1;
		}

		/// <summary>
		/// team.set_ally_mask(entityId, mask)
		/// Sets the full ally bitmask.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Team_SetAllyMask(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.teamLookup.HasComponent(entity))
				return 0;

			var mask = (int)Lua.lua_tointeger(l, 2);
			var team = ctx.teamLookup[entity];
			team.allyMask = mask;
			ctx.teamLookup[entity] = team;

			return 0;
		}

		/// <summary>
		/// team.set_enemy_mask(entityId, mask)
		/// Sets the full enemy bitmask.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Team_SetEnemyMask(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.teamLookup.HasComponent(entity))
				return 0;

			var mask = (int)Lua.lua_tointeger(l, 2);
			var team = ctx.teamLookup[entity];
			team.enemyMask = mask;
			ctx.teamLookup[entity] = team;

			return 0;
		}

		/// <summary>
		/// team.add_ally(entityId, otherTeamId)
		/// Adds a team to the ally mask.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Team_AddAlly(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.teamLookup.HasComponent(entity))
				return 0;

			var otherTeamId = (int)Lua.lua_tointeger(l, 2);
			var team = ctx.teamLookup[entity];
			team.AddAlly(otherTeamId);
			ctx.teamLookup[entity] = team;

			return 0;
		}

		/// <summary>
		/// team.add_enemy(entityId, otherTeamId)
		/// Adds a team to the enemy mask.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Team_AddEnemy(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);

			if (entity == Entity.Null)
				return 0;

			ref var ctx = ref s_context.Data;
			if (!ctx.isValid || !ctx.teamLookup.HasComponent(entity))
				return 0;

			var otherTeamId = (int)Lua.lua_tointeger(l, 2);
			var team = ctx.teamLookup[entity];
			team.AddEnemy(otherTeamId);
			ctx.teamLookup[entity] = team;

			return 0;
		}
	}
}
