namespace LuaGame.GameMode
{
	using System.Runtime.InteropServices;
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.Logging;

	/// <summary>
	/// Lua bridge for game mode operations.
	/// Exposes gamemode.* namespace to Lua scripts.
	/// </summary>
	public static class LuaGameModeBridge
	{
		static GameModeManager s_Manager;
		static string s_PendingModeSwitch;

		/// <summary>
		/// Sets the GameModeManager instance for bridge operations.
		/// Must be called before using gamemode.* functions from Lua.
		/// </summary>
		public static void SetManager(GameModeManager manager)
		{
			s_Manager = manager;
		}

		/// <summary>
		/// Gets any pending mode switch request and clears it.
		/// Call this from the game loop to process Lua-initiated mode switches.
		/// </summary>
		/// <returns>Mode name to switch to, or null if no switch pending</returns>
		public static string ConsumePendingModeSwitch()
		{
			var pending = s_PendingModeSwitch;
			s_PendingModeSwitch = null;
			return pending;
		}

		/// <summary>
		/// Checks if there's a pending mode switch.
		/// </summary>
		public static bool HasPendingModeSwitch => !string.IsNullOrEmpty(s_PendingModeSwitch);

		/// <summary>
		/// Registers gamemode.* functions with the Lua state.
		/// </summary>
		public static void RegisterFunctions(lua_State l)
		{
			Lua.lua_newtable(l);

			RegisterFunction(l, "load", GameMode_Load);
			RegisterFunction(l, "current", GameMode_Current);
			RegisterFunction(l, "is_loading", GameMode_IsLoading);

			Lua.lua_setglobal(l, "gamemode");
		}

		static void RegisterFunction(lua_State l, string name, Lua.lua_CFunction func)
		{
			Lua.lua_pushcfunction(l, func);
			Lua.lua_setfield(l, -2, name);
		}

		/// <summary>
		/// gamemode.load(name) - Request a mode switch.
		/// The switch is deferred and processed by the game loop.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int GameMode_Load(lua_State l)
		{
			var argCount = Lua.lua_gettop(l);
			if (argCount < 1)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			if (Lua.lua_isstring(l, 1) == 0)
			{
				Log.Warning("[GameMode] load() requires a string argument");
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var modeName = Lua.lua_tostring(l, 1);
			if (string.IsNullOrEmpty(modeName))
			{
				Log.Warning("[GameMode] load() mode name cannot be empty");
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			// Queue the mode switch for processing by the game loop
			s_PendingModeSwitch = modeName;
			Log.Info("[GameMode] Queued mode switch to: {0}", modeName);

			Lua.lua_pushboolean(l, 1);
			return 1;
		}

		/// <summary>
		/// gamemode.current() - Get the current mode name.
		/// Returns nil if no mode is active.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int GameMode_Current(lua_State l)
		{
			if (s_Manager == null || string.IsNullOrEmpty(s_Manager.CurrentMode))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			Lua.lua_pushstring(l, s_Manager.CurrentMode);
			return 1;
		}

		/// <summary>
		/// gamemode.is_loading() - Check if a mode transition is in progress.
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int GameMode_IsLoading(lua_State l)
		{
			var isLoading = s_Manager?.IsLoading ?? false;
			Lua.lua_pushboolean(l, isLoading ? 1 : 0);
			return 1;
		}
	}
}
