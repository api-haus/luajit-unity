namespace LuaECS.Core
{
	using AOT;
	using LuaECS.Components;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;

	public static partial class LuaECSBridge
	{
		internal static void RegisterSpatialFunctions(lua_State L)
		{
			RegisterFunction(L, "distance", ECS_Distance);
			RegisterFunction(L, "query_entities_near", ECS_QueryEntitiesNear);
			RegisterFunction(L, "get_entity_count", ECS_GetEntityCount);
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_Distance(lua_State L)
		{
			float3 posA,
				posB;

			if (Lua.lua_isnumber(L, 1) != 0)
			{
				var indexA = (int)Lua.lua_tointeger(L, 1);
				var entityA = GetEntityFromIdBurst(indexA);

				if (!TryGetTransformBurst(entityA, out var transformA))
				{
					Lua.lua_pushnumber(L, -1);
					return 1;
				}

				posA = transformA.Position;
			}
			else if (Lua.lua_istable(L, 1) != 0)
			{
				posA = TableToFloat3Burst(L, 1);
			}
			else
			{
				Lua.lua_pushnumber(L, -1);
				return 1;
			}

			if (Lua.lua_isnumber(L, 2) != 0)
			{
				var indexB = (int)Lua.lua_tointeger(L, 2);
				var entityB = GetEntityFromIdBurst(indexB);

				if (!TryGetTransformBurst(entityB, out var transformB))
				{
					Lua.lua_pushnumber(L, -1);
					return 1;
				}

				posB = transformB.Position;
			}
			else if (Lua.lua_istable(L, 2) != 0)
			{
				posB = TableToFloat3Burst(L, 2);
			}
			else
			{
				Lua.lua_pushnumber(L, -1);
				return 1;
			}

			var distance = math.distance(posA, posB);
			Lua.lua_pushnumber(L, distance);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int ECS_QueryEntitiesNear(lua_State L)
		{
			if (!s_Initialized)
			{
				Lua.lua_newtable(L);
				return 1;
			}

			float3 center;
			if (Lua.lua_istable(L, 1) != 0)
			{
				center = TableToFloat3Burst(L, 1);
			}
			else if (Lua.lua_isnumber(L, 1) != 0)
			{
				var entityIndex = (int)Lua.lua_tointeger(L, 1);
				var entity = GetEntityFromIdBurst(entityIndex);

				if (!TryGetTransformBurst(entity, out var transform))
				{
					Lua.lua_newtable(L);
					return 1;
				}

				center = transform.Position;
			}
			else
			{
				Lua.lua_newtable(L);
				return 1;
			}

			var radius = (float)Lua.lua_tonumber(L, 2);
			var radiusSq = radius * radius;

			var query = s_EntityManager.CreateEntityQuery(
				ComponentType.ReadOnly<LocalTransform>(),
				ComponentType.ReadOnly<LuaScript>()
			);

			var entities = query.ToEntityArray(Allocator.Temp);
			var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

			Lua.lua_newtable(L);
			var resultIndex = 1;

			for (var i = 0; i < entities.Length; i++)
			{
				var distSq = math.distancesq(center, transforms[i].Position);
				if (distSq <= radiusSq)
				{
					var entityId = LuaEntityRegistry.GetIdFromEntity(entities[i]);
					if (entityId > 0)
					{
						Lua.lua_pushinteger(L, entityId);
						Lua.lua_rawseti(L, -2, resultIndex++);
					}
				}
			}

			entities.Dispose();
			transforms.Dispose();

			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int ECS_GetEntityCount(lua_State L)
		{
			if (!s_Initialized)
			{
				Lua.lua_pushinteger(L, 0);
				return 1;
			}

			var query = s_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LuaScript>());
			var count = query.CalculateEntityCount();
			Lua.lua_pushinteger(L, count);
			return 1;
		}
	}
}
