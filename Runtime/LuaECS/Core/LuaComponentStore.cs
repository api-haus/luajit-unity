namespace LuaECS.Core
{
	using System.Collections.Generic;
	using AOT;
	using Components;
	using LuaNET.LuaJIT;
	using Unity.Entities;
	using Unity.Logging;

	/// <summary>
	/// Manages Lua-defined component types.
	/// Structural presence tracked via ECS tag pool (LuaDynTag0..63).
	/// Field data stored in Lua tables (__lua_comp[name][eid]).
	/// </summary>
	public static class LuaComponentStore
	{
		const int MaxSlots = 64;

		static readonly Dictionary<string, int> s_nameToSlot = new();
		static readonly string[] s_slotToName = new string[MaxSlots];
		static readonly Dictionary<string, Dictionary<string, string>> s_schemas = new();

		// Tracks which Lua components each entity has (for cleanup)
		static readonly Dictionary<int, HashSet<string>> s_entityComponents = new();

		// Tracks which entities already have LuaDataCleanup (avoid duplicate AddComponent)
		static readonly HashSet<int> s_entitiesWithCleanup = new();

		static int s_nextSlot;

		public static void Register(lua_State l)
		{
			// Get existing ecs table (created by LuaQueryBridge)
			Lua.lua_getglobal(l, "ecs");
			if (Lua.lua_istable(l, -1) == 0)
			{
				Lua.lua_pop(l, 1);
				Lua.lua_newtable(l);
				Lua.lua_setglobal(l, "ecs");
				Lua.lua_getglobal(l, "ecs");
			}

			Lua.lua_pushcfunction(l, EcsDefine);
			Lua.lua_setfield(l, -2, "define");

			Lua.lua_pushcfunction(l, EcsAdd);
			Lua.lua_setfield(l, -2, "add");

			Lua.lua_pushcfunction(l, EcsRemove);
			Lua.lua_setfield(l, -2, "remove");

			Lua.lua_pushcfunction(l, EcsHas);
			Lua.lua_setfield(l, -2, "has");

			Lua.lua_pop(l, 1); // pop ecs table

			// Inject Lua-side bootstrap: data store + fast accessor
			const string bootstrap = @"
				__lua_comp = __lua_comp or {}
				function ecs.get(eid, name)
					local store = __lua_comp[name]
					return store and store[eid]
				end
			";
			if (Lua.luaL_dostring(l, bootstrap) != Lua.LUA_OK)
			{
				var err = Lua.lua_tostring(l, -1);
				Log.Error("[LuaComponentStore] Bootstrap failed: {0}", err);
				Lua.lua_pop(l, 1);
			}
		}

		public static void Shutdown()
		{
			s_nameToSlot.Clear();
			s_schemas.Clear();
			s_entityComponents.Clear();
			s_entitiesWithCleanup.Clear();
			s_nextSlot = 0;

			for (var i = 0; i < MaxSlots; i++)
				s_slotToName[i] = null;
		}

		/// <summary>
		/// Returns the component names associated with an entity (for cleanup).
		/// </summary>
		public static HashSet<string> GetEntityComponents(int entityId)
		{
			return s_entityComponents.TryGetValue(entityId, out var set) ? set : null;
		}

		/// <summary>
		/// Removes all tracking for an entity. Called during cleanup.
		/// </summary>
		public static void CleanupEntity(int entityId)
		{
			s_entityComponents.Remove(entityId);
			s_entitiesWithCleanup.Remove(entityId);
		}

		/// <summary>
		/// Returns the slot name for a given slot index (for cleanup/debug).
		/// </summary>
		public static string GetSlotName(int slot)
		{
			return slot >= 0 && slot < MaxSlots ? s_slotToName[slot] : null;
		}

		/// <summary>
		/// Checks if a component name is Lua-defined.
		/// </summary>
		public static bool IsDefined(string name)
		{
			return s_nameToSlot.ContainsKey(name);
		}

		#region Bridge Functions

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int EcsDefine(lua_State l)
		{
			var name = Lua.lua_tostring(l, 1);
			if (string.IsNullOrEmpty(name))
				return Lua.luaL_error(l, "ecs.define: name required");

			// Check for collision with C#-defined components
			if (LuaComponentRegistry.TryGetComponentType(name, out _))
				return Lua.luaL_error(l, $"ecs.define: '{name}' already exists as a C# component");

			if (s_nameToSlot.ContainsKey(name))
				return Lua.luaL_error(l, $"ecs.define: '{name}' already defined");

			if (s_nextSlot >= MaxSlots)
				return Lua.luaL_error(l, $"ecs.define: tag pool exhausted (max {MaxSlots})");

			// Parse optional schema
			if (Lua.lua_gettop(l) >= 2 && Lua.lua_istable(l, 2) != 0)
			{
				var schema = new Dictionary<string, string>();
				Lua.lua_pushnil(l);
				while (Lua.lua_next(l, 2) != 0)
				{
					var field = Lua.lua_tostring(l, -2);
					var type = Lua.lua_tostring(l, -1);
					if (field != null && type != null)
						schema[field] = type;
					Lua.lua_pop(l, 1);
				}

				if (schema.Count > 0)
					s_schemas[name] = schema;
			}

			// Assign tag slot
			var slot = s_nextSlot++;
			s_nameToSlot[name] = slot;
			s_slotToName[slot] = name;

			// Register in LuaComponentRegistry so ecs.query() resolves this name
			var tagType = GetTagType(slot);
			LuaComponentRegistry.Register(name, tagType);

			// Create Lua-side storage table: __lua_comp[name] = {}
			Lua.lua_getglobal(l, "__lua_comp");
			Lua.lua_newtable(l);
			Lua.lua_setfield(l, -2, name);
			Lua.lua_pop(l, 1);

			Log.Info("[LuaComponentStore] Defined '{0}' → slot {1}", name, slot);
			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int EcsAdd(lua_State l)
		{
			var argCount = Lua.lua_gettop(l);
			var entityId = (int)Lua.lua_tointeger(l, 1);
			if (entityId <= 0)
				return Lua.luaL_error(l, "ecs.add: invalid entity id");

			var name = Lua.lua_tostring(l, 2);
			if (string.IsNullOrEmpty(name))
				return Lua.luaL_error(l, "ecs.add: component name required");

			if (!s_nameToSlot.TryGetValue(name, out var slot))
				return Lua.luaL_error(l, $"ecs.add: '{name}' not defined (call ecs.define first)");

			var hasDataTable = argCount >= 3 && Lua.lua_istable(l, 3) != 0;

			// Schema validation (strict: reject unknown fields, type-check values)
			if (hasDataTable && s_schemas.TryGetValue(name, out var schema))
			{
				var err = ValidateSchema(l, 3, name, schema);
				if (err != null)
					return Lua.luaL_error(l, err);
			}

			// Store data in __lua_comp[name][eid]
			Lua.lua_getglobal(l, "__lua_comp");
			Lua.lua_getfield(l, -1, name);
			if (hasDataTable)
				Lua.lua_pushvalue(l, 3);
			else
				Lua.lua_pushboolean(l, 1);

			Lua.lua_rawseti(l, -2, entityId);
			Lua.lua_pop(l, 2); // pop __lua_comp[name] and __lua_comp

			// Track entity → component mapping
			if (!s_entityComponents.TryGetValue(entityId, out var components))
			{
				components = new HashSet<string>();
				s_entityComponents[entityId] = components;
			}

			components.Add(name);

			// Add ECS tag via ECB
			if (!LuaECSBridge.TryGetBurstContextECB(out var ecb))
				return Lua.luaL_error(l, "ecs.add: no active ECB context");

			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);
			if (entity == Entity.Null)
				return Lua.luaL_error(l, $"ecs.add: entity {entityId} not found");

			ecb.AddComponent(entity, GetTagType(slot));

			// Add LuaDataCleanup if not already present
			if (s_entitiesWithCleanup.Add(entityId))
				ecb.AddComponent(entity, new LuaDataCleanup { entityId = entityId });

			// Return the data table (or true)
			if (hasDataTable)
				Lua.lua_pushvalue(l, 3);
			else
				Lua.lua_pushboolean(l, 1);

			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int EcsRemove(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			if (entityId <= 0)
				return Lua.luaL_error(l, "ecs.remove: invalid entity id");

			var name = Lua.lua_tostring(l, 2);
			if (string.IsNullOrEmpty(name))
				return Lua.luaL_error(l, "ecs.remove: component name required");

			if (!s_nameToSlot.TryGetValue(name, out var slot))
				return Lua.luaL_error(l, $"ecs.remove: '{name}' not defined");

			// Nil out __lua_comp[name][eid]
			Lua.lua_getglobal(l, "__lua_comp");
			Lua.lua_getfield(l, -1, name);
			Lua.lua_pushnil(l);
			Lua.lua_rawseti(l, -2, entityId);
			Lua.lua_pop(l, 2);

			// Update tracking
			if (s_entityComponents.TryGetValue(entityId, out var components))
				components.Remove(name);

			// Remove ECS tag via ECB
			if (!LuaECSBridge.TryGetBurstContextECB(out var ecb))
				return Lua.luaL_error(l, "ecs.remove: no active ECB context");

			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);
			if (entity == Entity.Null)
				return Lua.luaL_error(l, $"ecs.remove: entity {entityId} not found");

			ecb.RemoveComponent(entity, GetTagType(slot));

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int EcsHas(lua_State l)
		{
			var entityId = (int)Lua.lua_tointeger(l, 1);
			if (entityId <= 0)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var name = Lua.lua_tostring(l, 2);
			if (string.IsNullOrEmpty(name))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			// Check Lua-defined components via data store
			if (s_nameToSlot.ContainsKey(name))
			{
				if (s_entityComponents.TryGetValue(entityId, out var components)
					&& components.Contains(name))
				{
					Lua.lua_pushboolean(l, 1);
					return 1;
				}

				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			// Fall back to C#-defined components
			if (!LuaComponentRegistry.TryGetComponentType(name, out _))
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			// For C# components, we'd need EntityManager.HasComponent
			// which requires the entity. Check via entity lookup.
			var entity = LuaECSBridge.GetEntityFromIdBurst(entityId);
			if (entity == Entity.Null)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			// Cannot check HasComponent without EntityManager in this context.
			// For now, return true if entity exists (component presence is structural —
			// if you're querying for it, the entity was returned by ecs.query).
			// TODO: add EntityManager access for full HasComponent support
			Lua.lua_pushboolean(l, 1);
			return 1;
		}

		#endregion

		#region Schema Validation

		static string ValidateSchema(
			lua_State l,
			int tableIndex,
			string componentName,
			Dictionary<string, string> schema
		)
		{
			if (tableIndex < 0)
				tableIndex = Lua.lua_gettop(l) + tableIndex + 1;

			Lua.lua_pushnil(l);
			while (Lua.lua_next(l, tableIndex) != 0)
			{
				var key = Lua.lua_tostring(l, -2);
				if (key == null)
				{
					Lua.lua_pop(l, 2);
					return $"ecs.add('{componentName}'): non-string key in data table";
				}

				if (!schema.ContainsKey(key))
				{
					Lua.lua_pop(l, 2);
					return $"ecs.add('{componentName}'): unknown field '{key}'";
				}

				var expectedType = schema[key];
				var actualType = Lua.lua_typename(l, Lua.lua_type(l, -1));
				if (actualType != expectedType)
				{
					Lua.lua_pop(l, 2);
					return $"ecs.add('{componentName}'): field '{key}' expected {expectedType}, got {actualType}";
				}

				Lua.lua_pop(l, 1); // pop value, keep key
			}

			return null;
		}

		#endregion

		#region Tag Pool Dispatch

		// @formatter:off
		static ComponentType GetTagType(int slot) => slot switch
		{
			0  => ComponentType.ReadWrite<LuaDynTag0>(),  1  => ComponentType.ReadWrite<LuaDynTag1>(),
			2  => ComponentType.ReadWrite<LuaDynTag2>(),  3  => ComponentType.ReadWrite<LuaDynTag3>(),
			4  => ComponentType.ReadWrite<LuaDynTag4>(),  5  => ComponentType.ReadWrite<LuaDynTag5>(),
			6  => ComponentType.ReadWrite<LuaDynTag6>(),  7  => ComponentType.ReadWrite<LuaDynTag7>(),
			8  => ComponentType.ReadWrite<LuaDynTag8>(),  9  => ComponentType.ReadWrite<LuaDynTag9>(),
			10 => ComponentType.ReadWrite<LuaDynTag10>(), 11 => ComponentType.ReadWrite<LuaDynTag11>(),
			12 => ComponentType.ReadWrite<LuaDynTag12>(), 13 => ComponentType.ReadWrite<LuaDynTag13>(),
			14 => ComponentType.ReadWrite<LuaDynTag14>(), 15 => ComponentType.ReadWrite<LuaDynTag15>(),
			16 => ComponentType.ReadWrite<LuaDynTag16>(), 17 => ComponentType.ReadWrite<LuaDynTag17>(),
			18 => ComponentType.ReadWrite<LuaDynTag18>(), 19 => ComponentType.ReadWrite<LuaDynTag19>(),
			20 => ComponentType.ReadWrite<LuaDynTag20>(), 21 => ComponentType.ReadWrite<LuaDynTag21>(),
			22 => ComponentType.ReadWrite<LuaDynTag22>(), 23 => ComponentType.ReadWrite<LuaDynTag23>(),
			24 => ComponentType.ReadWrite<LuaDynTag24>(), 25 => ComponentType.ReadWrite<LuaDynTag25>(),
			26 => ComponentType.ReadWrite<LuaDynTag26>(), 27 => ComponentType.ReadWrite<LuaDynTag27>(),
			28 => ComponentType.ReadWrite<LuaDynTag28>(), 29 => ComponentType.ReadWrite<LuaDynTag29>(),
			30 => ComponentType.ReadWrite<LuaDynTag30>(), 31 => ComponentType.ReadWrite<LuaDynTag31>(),
			32 => ComponentType.ReadWrite<LuaDynTag32>(), 33 => ComponentType.ReadWrite<LuaDynTag33>(),
			34 => ComponentType.ReadWrite<LuaDynTag34>(), 35 => ComponentType.ReadWrite<LuaDynTag35>(),
			36 => ComponentType.ReadWrite<LuaDynTag36>(), 37 => ComponentType.ReadWrite<LuaDynTag37>(),
			38 => ComponentType.ReadWrite<LuaDynTag38>(), 39 => ComponentType.ReadWrite<LuaDynTag39>(),
			40 => ComponentType.ReadWrite<LuaDynTag40>(), 41 => ComponentType.ReadWrite<LuaDynTag41>(),
			42 => ComponentType.ReadWrite<LuaDynTag42>(), 43 => ComponentType.ReadWrite<LuaDynTag43>(),
			44 => ComponentType.ReadWrite<LuaDynTag44>(), 45 => ComponentType.ReadWrite<LuaDynTag45>(),
			46 => ComponentType.ReadWrite<LuaDynTag46>(), 47 => ComponentType.ReadWrite<LuaDynTag47>(),
			48 => ComponentType.ReadWrite<LuaDynTag48>(), 49 => ComponentType.ReadWrite<LuaDynTag49>(),
			50 => ComponentType.ReadWrite<LuaDynTag50>(), 51 => ComponentType.ReadWrite<LuaDynTag51>(),
			52 => ComponentType.ReadWrite<LuaDynTag52>(), 53 => ComponentType.ReadWrite<LuaDynTag53>(),
			54 => ComponentType.ReadWrite<LuaDynTag54>(), 55 => ComponentType.ReadWrite<LuaDynTag55>(),
			56 => ComponentType.ReadWrite<LuaDynTag56>(), 57 => ComponentType.ReadWrite<LuaDynTag57>(),
			58 => ComponentType.ReadWrite<LuaDynTag58>(), 59 => ComponentType.ReadWrite<LuaDynTag59>(),
			60 => ComponentType.ReadWrite<LuaDynTag60>(), 61 => ComponentType.ReadWrite<LuaDynTag61>(),
			62 => ComponentType.ReadWrite<LuaDynTag62>(), 63 => ComponentType.ReadWrite<LuaDynTag63>(),
			_ => throw new System.InvalidOperationException($"Tag pool slot {slot} out of range"),
		};
		// @formatter:on

		#endregion

		/// <summary>
		/// Scrubs Lua-side data for an entity. Call from cleanup system with access to lua_State.
		/// </summary>
		public static void ScrubLuaData(lua_State l, int entityId, HashSet<string> componentNames)
		{
			Lua.lua_getglobal(l, "__lua_comp");
			if (Lua.lua_istable(l, -1) == 0)
			{
				Lua.lua_pop(l, 1);
				return;
			}

			foreach (var name in componentNames)
			{
				Lua.lua_getfield(l, -1, name);
				if (Lua.lua_istable(l, -1) != 0)
				{
					Lua.lua_pushnil(l);
					Lua.lua_rawseti(l, -2, entityId);
				}

				Lua.lua_pop(l, 1);
			}

			Lua.lua_pop(l, 1); // pop __lua_comp
		}
	}
}
