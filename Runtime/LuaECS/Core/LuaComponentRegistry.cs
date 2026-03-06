namespace LuaECS.Core
{
	using System;
	using System.Collections.Generic;
	using LuaNET.LuaJIT;
	using Unity.Entities;

	public delegate void LuaLookupUpdater(ref SystemState state);

	public static class LuaComponentRegistry
	{
		static readonly Dictionary<string, ComponentType> s_components = new();
		static readonly List<Action<lua_State>> s_bridgeRegistrations = new();
		static readonly List<LuaLookupUpdater> s_lookupUpdaters = new();

		public static void Register(string luaName, ComponentType componentType)
		{
			s_components[luaName] = componentType;
		}

		public static void RegisterBridge(
			string luaName,
			ComponentType componentType,
			Action<lua_State> registerFunc,
			LuaLookupUpdater updateLookupFunc
		)
		{
			s_components[luaName] = componentType;
			s_bridgeRegistrations.Add(registerFunc);
			s_lookupUpdaters.Add(updateLookupFunc);
		}

		public static bool TryGetComponentType(string luaName, out ComponentType componentType)
		{
			return s_components.TryGetValue(luaName, out componentType);
		}

		public static void RegisterAllBridges(lua_State l)
		{
			foreach (var reg in s_bridgeRegistrations)
			{
				reg(l);
			}
		}

		public static void UpdateAllLookups(ref SystemState state)
		{
			foreach (var updater in s_lookupUpdaters)
			{
				updater(ref state);
			}
		}
	}
}
