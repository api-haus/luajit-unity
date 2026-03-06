namespace LuaECS.Core
{
	using System.Collections.Generic;
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.Collections;
	using Unity.Entities;

	public static class LuaQueryBridge
	{
		static readonly Dictionary<int, EntityQuery> s_queryCache = new();
		static EntityManager s_entityManager;
		static bool s_initialized;

		public static void Initialize(EntityManager entityManager)
		{
			s_entityManager = entityManager;
			s_initialized = true;
		}

		public static void Shutdown()
		{
			foreach (var kvp in s_queryCache)
			{
				if (kvp.Value != default)
					kvp.Value.Dispose();
			}
			s_queryCache.Clear();
			s_initialized = false;
		}

		public static void Register(lua_State l)
		{
			Lua.lua_newtable(l);
			Lua.lua_pushcfunction(l, Query);
			Lua.lua_setfield(l, -2, "query");
			Lua.lua_setglobal(l, "ecs");
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Query(lua_State l)
		{
			if (!s_initialized)
			{
				Lua.lua_newtable(l);
				return 1;
			}

			var argCount = Lua.lua_gettop(l);
			var allComponents = new List<ComponentType>();
			var noneComponents = new List<ComponentType>();

			if (argCount >= 1 && Lua.lua_istable(l, 1) != 0)
			{
				// Table form: { all = {...}, none = {...} }
				Lua.lua_getfield(l, 1, "all");
				if (Lua.lua_istable(l, -1) != 0)
					ReadComponentList(l, -1, allComponents);
				Lua.lua_pop(l, 1);

				Lua.lua_getfield(l, 1, "none");
				if (Lua.lua_istable(l, -1) != 0)
					ReadComponentList(l, -1, noneComponents);
				Lua.lua_pop(l, 1);

				// If no "all" key, treat the table as simple array of component names
				if (allComponents.Count == 0)
				{
					ReadComponentList(l, 1, allComponents);
				}
			}
			else
			{
				// Varargs form: query("comp1", "comp2", ...)
				for (var i = 1; i <= argCount; i++)
				{
					var name = Lua.lua_tostring(l, i);
					if (name != null && LuaComponentRegistry.TryGetComponentType(name, out var ct))
					{
						allComponents.Add(ct);
					}
				}
			}

			if (allComponents.Count == 0)
			{
				Lua.lua_newtable(l);
				return 1;
			}

			var query = GetOrCreateQuery(allComponents, noneComponents);
			var entities = query.ToEntityArray(Allocator.Temp);

			// Build result table of entity IDs
			Lua.lua_newtable(l);
			var resultIndex = 1;
			for (var i = 0; i < entities.Length; i++)
			{
				var entity = entities[i];
				var entityId = LuaEntityRegistry.GetIdFromEntity(entity);
				if (entityId <= 0)
				{
					// Try to get from EntityManager
					if (s_entityManager.HasComponent<Components.LuaEntityId>(entity))
					{
						entityId = s_entityManager.GetComponentData<Components.LuaEntityId>(entity).value;
					}
				}

				if (entityId > 0)
				{
					Lua.lua_pushinteger(l, entityId);
					Lua.lua_rawseti(l, -2, resultIndex++);
				}
			}
			entities.Dispose();

			return 1;
		}

		static void ReadComponentList(lua_State l, int tableIndex, List<ComponentType> result)
		{
			if (tableIndex < 0)
				tableIndex = Lua.lua_gettop(l) + tableIndex + 1;

			var len = (int)Lua.lua_objlen(l, tableIndex);
			for (var i = 1; i <= len; i++)
			{
				Lua.lua_rawgeti(l, tableIndex, i);
				var name = Lua.lua_tostring(l, -1);
				if (name != null && LuaComponentRegistry.TryGetComponentType(name, out var ct))
				{
					result.Add(ct);
				}
				Lua.lua_pop(l, 1);
			}
		}

		static EntityQuery GetOrCreateQuery(
			List<ComponentType> allComponents,
			List<ComponentType> noneComponents
		)
		{
			var hash = ComputeQueryHash(allComponents, noneComponents);
			if (s_queryCache.TryGetValue(hash, out var cached))
				return cached;

			var desc = new EntityQueryDesc
			{
				All = allComponents.ToArray(),
				None = noneComponents.Count > 0 ? noneComponents.ToArray() : System.Array.Empty<ComponentType>(),
			};

			var query = s_entityManager.CreateEntityQuery(desc);
			s_queryCache[hash] = query;
			return query;
		}

		static int ComputeQueryHash(
			List<ComponentType> allComponents,
			List<ComponentType> noneComponents
		)
		{
			var hash = 17;
			foreach (var ct in allComponents)
				hash = hash * 31 + ct.TypeIndex.GetHashCode();
			hash = hash * 31 + 0x7F7F;
			foreach (var ct in noneComponents)
				hash = hash * 31 + ct.TypeIndex.GetHashCode();
			return hash;
		}
	}
}
