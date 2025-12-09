namespace LuaVM.Core
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using LuaNET.LuaJIT;
	using Unity.Logging;
	using UnityEngine;

	/// <summary>
	/// Delegate for registering custom bridge functions with the Lua VM.
	/// Called during VM initialization after standard libraries are loaded.
	/// </summary>
	public delegate void LuaBridgeRegistration(lua_State l);

	/// <summary>
	/// Manages Lua VM lifecycle, script loading, and execution.
	/// Designed to be used standalone or with ECS integration.
	/// </summary>
	public sealed class LuaVMManager : IDisposable
	{
		public static LuaVMManager Instance { get; private set; }

		lua_State m_State;
		readonly Dictionary<string, int> m_ScriptRefs = new();
		readonly Dictionary<string, int> m_StateRefs = new();
		readonly List<LuaBridgeRegistration> m_BridgeRegistrations = new();
		bool m_Disposed;
		readonly string m_BasePath;

		public lua_State State => m_State;
		public bool IsValid => m_State.IsNotNull;

		/// <summary>
		/// Creates a new LuaVMManager with optional custom base path.
		/// </summary>
		/// <param name="basePath">Base path for Lua scripts. Defaults to StreamingAssets/lua</param>
		public LuaVMManager(string basePath = null)
		{
			if (Instance != null && Instance.IsValid)
			{
				Log.Warning("[LuaVM] Instance already exists, reusing existing VM");
				return;
			}

			Instance?.Dispose();

			Instance = this;
			m_BasePath = basePath ?? Path.Combine(Application.streamingAssetsPath, "lua");

			// Register custom scripts path with highest priority if provided
			if (basePath != null)
			{
				var scriptsPath = Path.Combine(m_BasePath, "scripts");
				if (Directory.Exists(scriptsPath))
				{
					LuaScriptSearchPaths.AddSearchPath(scriptsPath, priority: 0);
				}
			}

			Initialize();
		}

		public static LuaVMManager GetOrCreate(string basePath = null)
		{
			if (Instance != null && Instance.IsValid)
				return Instance;

			return new LuaVMManager(basePath);
		}

		/// <summary>
		/// Register bridge functions immediately on an already-initialized VM.
		/// </summary>
		public void RegisterBridgeNow(LuaBridgeRegistration registration)
		{
			if (m_State.IsNull)
			{
				Log.Error("[LuaVM] Cannot register bridge - VM not initialized");
				return;
			}

			registration(m_State);
		}

		void Initialize()
		{
			m_State = Lua.luaL_newstate();
			if (m_State.IsNull)
				throw new InvalidOperationException("Failed to create Lua state");

			Lua.luaL_openlibs(m_State);
			SetupPackagePath();

			foreach (var registration in m_BridgeRegistrations)
			{
				registration(m_State);
			}

			LoadBootstrapScript();
		}

		void SetupPackagePath()
		{
			var luaPath = m_BasePath;
			var dataPath = Path.Combine(luaPath, "data");
			var scriptsPath = Path.Combine(luaPath, "scripts");

			var pathSetup =
				$@"
				package.path = package.path .. ';{luaPath.Replace("\\", "/")}/?/init.lua'
				package.path = package.path .. ';{luaPath.Replace("\\", "/")}/?.lua'
				package.path = package.path .. ';{dataPath.Replace("\\", "/")}/?.lua'
				package.path = package.path .. ';{scriptsPath.Replace("\\", "/")}/?.lua'
			";

			var result = Lua.luaL_dostring(m_State, pathSetup);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(m_State, -1);
				Log.Error("[LuaVM] Failed to set package path: {0}", error);
				Lua.lua_pop(m_State, 1);
			}
		}

		void LoadBootstrapScript()
		{
			var bootstrapPath = Path.Combine(m_BasePath, "bootstrap.lua");

			if (!File.Exists(bootstrapPath))
			{
				Log.Warning("[LuaVM] bootstrap.lua not found at {0}", bootstrapPath);
				return;
			}

			var result = Lua.luaL_dofile(m_State, bootstrapPath);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(m_State, -1);
				Log.Error("[LuaVM] Failed to load bootstrap.lua: {0}", error);
				Lua.lua_pop(m_State, 1);
			}
			else
			{
				Log.Info("[LuaVM] Bootstrap script loaded successfully");
			}
		}

		/// <summary>
		/// Load a script by name, searching all registered paths.
		/// </summary>
		/// <param name="scriptName">Script name (e.g., "player" or "enemies/goblin").</param>
		/// <returns>True if script loaded successfully.</returns>
		public bool LoadScript(string scriptName)
		{
			var loadResult = LuaScriptLoader.FromSearchPaths(scriptName);
			return LoadScript(loadResult);
		}

		/// <summary>
		/// Load a script from a LuaScriptLoadResult.
		/// Only works for file-based sources (StreamingAssets, FilePath).
		/// </summary>
		/// <param name="loadResult">Result from LuaScriptLoader.</param>
		/// <returns>True if script loaded successfully.</returns>
		public bool LoadScript(LuaScriptLoadResult loadResult)
		{
			if (!loadResult.isValid)
			{
				Log.Error("[LuaVM] Invalid load result: {0}", loadResult.error.ToString());
				return false;
			}

			var scriptId = loadResult.scriptId.ToString();
			if (m_ScriptRefs.ContainsKey(scriptId))
				return true;

			return loadResult.sourceType switch
			{
				LuaScriptSourceType.STREAMING_ASSETS or LuaScriptSourceType.FILE_PATH => LoadScriptFromFile(
					scriptId,
					loadResult.filePath.ToString()
				),
				_ => false,
			};
		}

		/// <summary>
		/// Load a script from a raw Lua source string.
		/// </summary>
		/// <param name="scriptId">Unique identifier for the script.</param>
		/// <param name="source">Raw Lua source code.</param>
		/// <returns>True if script loaded successfully.</returns>
		public bool LoadScriptFromString(string scriptId, string source)
		{
			if (string.IsNullOrEmpty(scriptId))
			{
				Log.Error("[LuaVM] Script ID cannot be empty");
				return false;
			}

			if (m_ScriptRefs.ContainsKey(scriptId))
				return true;

			return LoadScriptFromSource(scriptId, source);
		}

		/// <summary>
		/// Load a script from a Unity TextAsset.
		/// </summary>
		/// <param name="asset">TextAsset containing Lua code.</param>
		/// <returns>True if script loaded successfully.</returns>
		public bool LoadScriptFromTextAsset(TextAsset asset)
		{
			var validation = LuaScriptLoader.ValidateTextAsset(asset);
			if (!validation.isValid)
			{
				Log.Error("[LuaVM] Invalid TextAsset: {0}", validation.error.ToString());
				return false;
			}

			var scriptId = validation.scriptId.ToString();
			if (m_ScriptRefs.ContainsKey(scriptId))
				return true;

			return LoadScriptFromSource(scriptId, asset.text);
		}

		/// <summary>
		/// Load a script from a Unity TextAsset with custom script ID.
		/// </summary>
		/// <param name="scriptId">Custom script identifier.</param>
		/// <param name="asset">TextAsset containing Lua code.</param>
		/// <returns>True if script loaded successfully.</returns>
		public bool LoadScriptFromTextAsset(string scriptId, TextAsset asset)
		{
			var validation = LuaScriptLoader.ValidateTextAsset(scriptId, asset);
			if (!validation.isValid)
			{
				Log.Error("[LuaVM] Invalid TextAsset: {0}", validation.error.ToString());
				return false;
			}

			if (m_ScriptRefs.ContainsKey(scriptId))
				return true;

			return LoadScriptFromSource(scriptId, asset.text);
		}

		/// <summary>
		/// Load a script from an arbitrary file path.
		/// </summary>
		/// <param name="filePath">Absolute or relative file path.</param>
		/// <returns>True if script loaded successfully.</returns>
		public bool LoadScriptFromFilePath(string filePath)
		{
			var loadResult = LuaScriptLoader.FromFile(filePath);
			return LoadScript(loadResult);
		}

		bool LoadScriptFromSource(string scriptId, string source)
		{
			if (string.IsNullOrEmpty(source))
			{
				Log.Error("[LuaVM] Source is empty for script '{0}'", scriptId);
				return false;
			}

			var escapedSource = source
				.Replace("\\", "\\\\")
				.Replace("\"", "\\\"")
				.Replace("\n", "\\n")
				.Replace("\r", "\\r");
			var setupCode =
				$@"
				local env = setmetatable({{}}, {{ __index = _G }})
				local chunk, err = loadstring(""{escapedSource}"", '{scriptId}')
				if not chunk then
					error('Failed to load {scriptId}: ' .. tostring(err))
				end
				setfenv(chunk, env)
				chunk()
				return env
			";

			var result = Lua.luaL_dostring(m_State, setupCode);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(m_State, -1);
				Log.Error("[LuaVM] Failed to load script '{0}': {1}", scriptId, error);
				Lua.lua_pop(m_State, 1);
				return false;
			}

			if (Lua.lua_istable(m_State, -1) == 0)
			{
				Log.Error("[LuaVM] Script '{0}' environment is not a table", scriptId);
				Lua.lua_pop(m_State, 1);
				return false;
			}

			var scriptRef = Lua.luaL_ref(m_State, Lua.LUA_REGISTRYINDEX);
			m_ScriptRefs[scriptId] = scriptRef;

			Log.Verbose("[LuaVM] Loaded script '{0}' from source with ref {1}", scriptId, scriptRef);
			return true;
		}

		bool LoadScriptFromFile(string scriptId, string filePath)
		{
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				Log.Error("[LuaVM] File not found: {0}", filePath);
				return false;
			}

			var normalizedPath = filePath.Replace("\\", "/");
			var setupCode =
				$@"
				local env = setmetatable({{}}, {{ __index = _G }})
				local chunk, err = loadfile('{normalizedPath}')
				if not chunk then
					error('Failed to load {scriptId}: ' .. tostring(err))
				end
				setfenv(chunk, env)
				chunk()
				return env
			";

			var result = Lua.luaL_dostring(m_State, setupCode);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(m_State, -1);
				Log.Error("[LuaVM] Failed to load script '{0}' from '{1}': {2}", scriptId, filePath, error);
				Lua.lua_pop(m_State, 1);
				return false;
			}

			if (Lua.lua_istable(m_State, -1) == 0)
			{
				Log.Error("[LuaVM] Script '{0}' environment is not a table", scriptId);
				Lua.lua_pop(m_State, 1);
				return false;
			}

			var scriptRef = Lua.luaL_ref(m_State, Lua.LUA_REGISTRYINDEX);
			m_ScriptRefs[scriptId] = scriptRef;

			Log.Verbose("[LuaVM] Loaded script '{0}' from file with ref {1}", scriptId, scriptRef);
			return true;
		}

		/// <summary>
		/// Reload a script by name, searching all registered paths.
		/// </summary>
		public bool ReloadScript(string scriptName)
		{
			var loadResult = LuaScriptLoader.FromSearchPaths(scriptName);
			return ReloadScript(loadResult);
		}

		/// <summary>
		/// Reload a script from a LuaScriptLoadResult.
		/// Unloads the existing script first if present.
		/// </summary>
		public bool ReloadScript(LuaScriptLoadResult loadResult)
		{
			if (!loadResult.isValid)
			{
				Log.Error("[LuaVM] Invalid load result for reload: {0}", loadResult.error.ToString());
				return false;
			}

			var scriptId = loadResult.scriptId.ToString();

			if (m_ScriptRefs.TryGetValue(scriptId, out var oldRef))
			{
				Lua.luaL_unref(m_State, Lua.LUA_REGISTRYINDEX, oldRef);
				m_ScriptRefs.Remove(scriptId);
			}

			var clearCache = $"package.loaded['{scriptId}'] = nil";
			Lua.luaL_dostring(m_State, clearCache);

			return LoadScript(loadResult);
		}

		public int CreateState(string scriptName, int instanceId)
		{
			// Verify stack is clean before creating state
			var stackBefore = Lua.lua_gettop(m_State);

			Lua.lua_newtable(m_State);

			// Verify we created a table
			if (Lua.lua_type(m_State, -1) != Lua.LUA_TTABLE)
			{
				Log.Error(
					"[LuaVM] CreateState: newtable did not create table for {0}:{1}",
					scriptName,
					instanceId
				);
				Lua.lua_pop(m_State, 1);
				return -1;
			}

			Lua.lua_pushinteger(m_State, instanceId);
			Lua.lua_setfield(m_State, -2, "_instance");

			// Also set as _entity for ECS compatibility
			Lua.lua_pushinteger(m_State, instanceId);
			Lua.lua_setfield(m_State, -2, "_entity");

			Lua.lua_pushstring(m_State, scriptName);
			Lua.lua_setfield(m_State, -2, "_script");

			// Verify table is still on top
			if (Lua.lua_type(m_State, -1) != Lua.LUA_TTABLE)
			{
				Log.Error(
					"[LuaVM] CreateState: stack corrupted before ref for {0}:{1}",
					scriptName,
					instanceId
				);
				Lua.lua_pop(m_State, 1);
				return -1;
			}

			var stateRef = Lua.luaL_ref(m_State, Lua.LUA_REGISTRYINDEX);

			var key = $"{scriptName}:{instanceId}";

			// Check for duplicate key - this would indicate a bug
			if (m_StateRefs.TryGetValue(key, out var existingRef))
			{
				Log.Warning(
					"[LuaVM] CreateState: Overwriting existing state for {0} (old ref={1}, new ref={2})",
					key,
					existingRef,
					stateRef
				);
				// Unref the old one to prevent leak
				Lua.luaL_unref(m_State, Lua.LUA_REGISTRYINDEX, existingRef);
			}

			m_StateRefs[key] = stateRef;

			// Verify stack is clean after creating state
			var stackAfter = Lua.lua_gettop(m_State);
			if (stackAfter != stackBefore)
			{
				Log.Warning(
					"[LuaVM] CreateState: stack leak for {0}:{1}, before={2} after={3}",
					scriptName,
					instanceId,
					stackBefore,
					stackAfter
				);
			}

			Log.Verbose(
				"[LuaVM] Created state for {0}:{1} with ref={2}",
				scriptName,
				instanceId,
				stateRef
			);

			return stateRef;
		}

		/// <summary>
		/// Creates entity state - alias for CreateState for ECS compatibility.
		/// </summary>
		public int CreateEntityState(string scriptName, int entityIndex)
		{
			return CreateState(scriptName, entityIndex);
		}

		public void ReleaseState(string scriptName, int instanceId, int stateRef)
		{
			var key = $"{scriptName}:{instanceId}";
			m_StateRefs.Remove(key);
			Lua.luaL_unref(m_State, Lua.LUA_REGISTRYINDEX, stateRef);
		}

		/// <summary>
		/// Validates that a stateRef is the correct one for the given script/instance.
		/// Returns true if valid, false if mismatched or not found.
		/// </summary>
		public bool ValidateStateRef(string scriptName, int instanceId, int stateRef)
		{
			var key = $"{scriptName}:{instanceId}";
			if (!m_StateRefs.TryGetValue(key, out var expectedRef))
				return false;
			return expectedRef == stateRef;
		}

		/// <summary>
		/// Releases entity state - alias for ReleaseState for ECS compatibility.
		/// </summary>
		public void ReleaseEntityState(string scriptName, int entityIndex, int stateRef)
		{
			ReleaseState(scriptName, entityIndex, stateRef);
		}

		public bool CallFunction(
			string scriptName,
			string functionName,
			int instanceId,
			int stateRef,
			params object[] args
		)
		{
			if (!m_ScriptRefs.TryGetValue(scriptName, out var scriptRef))
			{
				Log.Error("[LuaVM] Script '{0}' not loaded", scriptName);
				return false;
			}

			Lua.lua_rawgeti(m_State, Lua.LUA_REGISTRYINDEX, scriptRef);
			Lua.lua_getfield(m_State, -1, functionName);

			if (Lua.lua_isfunction(m_State, -1) == 0)
			{
				Lua.lua_pop(m_State, 2);
				return true;
			}

			Lua.lua_pushinteger(m_State, instanceId);
			Lua.lua_rawgeti(m_State, Lua.LUA_REGISTRYINDEX, stateRef);

			// Debug: Check if state is actually a table
			var stateType = Lua.lua_type(m_State, -1);
			if (stateType != Lua.LUA_TTABLE)
			{
				// Get more info about what's actually at this registry index
				var stateTypeName = stateType switch
				{
					Lua.LUA_TNIL => "nil",
					Lua.LUA_TBOOLEAN => "boolean",
					Lua.LUA_TNUMBER => $"number({Lua.lua_tonumber(m_State, -1)})",
					Lua.LUA_TSTRING => $"string({Lua.lua_tostring(m_State, -1)})",
					Lua.LUA_TTABLE => "table",
					Lua.LUA_TFUNCTION => "function",
					_ => $"type{stateType}",
				};

				Log.Error(
					"[LuaVM] State for {0}.{1} is {2} (expected table), stateRef={3}, instanceId={4}",
					scriptName,
					functionName,
					stateTypeName,
					stateRef,
					instanceId
				);

				// Remove corrupt entry so future updates skip it
				var key = $"{scriptName}:{instanceId}";
				m_StateRefs.Remove(key);
				Lua.luaL_unref(m_State, Lua.LUA_REGISTRYINDEX, stateRef);

				// Clean up stack: script table, function, instanceId, bad state
				Lua.lua_pop(m_State, 4);
				return false;
			}

			var argCount = 2;
			foreach (var arg in args)
			{
				PushValue(arg);
				argCount++;
			}

			var result = Lua.lua_pcall(m_State, argCount, 0, 0);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(m_State, -1);
				Log.Error("[LuaVM] Error in {0}.{1}: {2}", scriptName, functionName, error);
				Lua.lua_pop(m_State, 1);
			}

			Lua.lua_pop(m_State, 1);
			return result == Lua.LUA_OK;
		}

		void PushValue(object value)
		{
			switch (value)
			{
				case null:
					Lua.lua_pushnil(m_State);
					break;
				case bool b:
					Lua.lua_pushboolean(m_State, b ? 1 : 0);
					break;
				case int i:
					Lua.lua_pushinteger(m_State, i);
					break;
				case long l:
					Lua.lua_pushinteger(m_State, l);
					break;
				case float f:
					Lua.lua_pushnumber(m_State, f);
					break;
				case double d:
					Lua.lua_pushnumber(m_State, d);
					break;
				case string s:
					Lua.lua_pushstring(m_State, s);
					break;
				default:
					Lua.lua_pushstring(m_State, value.ToString());
					break;
			}
		}

		public bool CallInit(string scriptName, int instanceId, int stateRef)
		{
			return CallFunction(scriptName, "OnInit", instanceId, stateRef);
		}

		public bool CallTick(string scriptName, int instanceId, int stateRef, float deltaTime)
		{
			return CallFunction(scriptName, "OnTick", instanceId, stateRef, deltaTime);
		}

		public bool CallCommand(string scriptName, int instanceId, int stateRef, string command)
		{
			if (!m_ScriptRefs.TryGetValue(scriptName, out var scriptRef))
				return false;

			Lua.lua_rawgeti(m_State, Lua.LUA_REGISTRYINDEX, scriptRef);
			Lua.lua_getfield(m_State, -1, "OnCommand");

			if (Lua.lua_isfunction(m_State, -1) == 0)
			{
				Lua.lua_pop(m_State, 2);
				return true;
			}

			Lua.lua_pushinteger(m_State, instanceId);
			Lua.lua_rawgeti(m_State, Lua.LUA_REGISTRYINDEX, stateRef);
			Lua.lua_pushstring(m_State, command);

			var result = Lua.lua_pcall(m_State, 3, 0, 0);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(m_State, -1);
				Log.Error("[LuaVM] Error in OnCommand: {0}", error);
				Lua.lua_pop(m_State, 1);
			}

			Lua.lua_pop(m_State, 1);
			return result == Lua.LUA_OK;
		}

		public bool CallEvent(
			string scriptName,
			int instanceId,
			int stateRef,
			string eventName,
			int sourceId,
			int targetId,
			int intParam
		)
		{
			if (!m_ScriptRefs.TryGetValue(scriptName, out var scriptRef))
				return false;

			Lua.lua_rawgeti(m_State, Lua.LUA_REGISTRYINDEX, scriptRef);
			Lua.lua_getfield(m_State, -1, eventName);

			if (Lua.lua_isfunction(m_State, -1) == 0)
			{
				Lua.lua_pop(m_State, 2);
				return true;
			}

			Lua.lua_pushinteger(m_State, instanceId);
			Lua.lua_rawgeti(m_State, Lua.LUA_REGISTRYINDEX, stateRef);

			Lua.lua_newtable(m_State);
			Lua.lua_pushinteger(m_State, sourceId);
			Lua.lua_setfield(m_State, -2, "source");
			Lua.lua_pushinteger(m_State, targetId);
			Lua.lua_setfield(m_State, -2, "target");
			Lua.lua_pushinteger(m_State, intParam);
			Lua.lua_setfield(m_State, -2, "param");

			var result = Lua.lua_pcall(m_State, 3, 0, 0);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(m_State, -1);
				Log.Error("[LuaVM] Error in {0}: {1}", eventName, error);
				Lua.lua_pop(m_State, 1);
			}

			Lua.lua_pop(m_State, 1);
			return result == Lua.LUA_OK;
		}

		public object LoadDataTable(string dataName)
		{
			var code = $"return require('data.{dataName}')";
			var result = Lua.luaL_dostring(m_State, code);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(m_State, -1);
				Log.Error("[LuaVM] Failed to load data '{0}': {1}", dataName, error);
				Lua.lua_pop(m_State, 1);
				return null;
			}

			var data = ConvertLuaValue(-1);
			Lua.lua_pop(m_State, 1);
			return data;
		}

		object ConvertLuaValue(int index)
		{
			var type = Lua.lua_type(m_State, index);

			switch (type)
			{
				case Lua.LUA_TNIL:
					return null;

				case Lua.LUA_TBOOLEAN:
					return Lua.lua_toboolean(m_State, index) != 0;

				case Lua.LUA_TNUMBER:
					return Lua.lua_tonumber(m_State, index);

				case Lua.LUA_TSTRING:
					return Lua.lua_tostring(m_State, index);

				case Lua.LUA_TTABLE:
					return ConvertLuaTable(index);

				default:
					return null;
			}
		}

		Dictionary<string, object> ConvertLuaTable(int index)
		{
			var dict = new Dictionary<string, object>();

			if (index < 0)
				index = Lua.lua_gettop(m_State) + index + 1;

			Lua.lua_pushnil(m_State);
			while (Lua.lua_next(m_State, index) != 0)
			{
				string key;
				if (Lua.lua_type(m_State, -2) == Lua.LUA_TSTRING)
					key = Lua.lua_tostring(m_State, -2);
				else if (Lua.lua_type(m_State, -2) == Lua.LUA_TNUMBER)
					key = Lua.lua_tointeger(m_State, -2).ToString();
				else
				{
					Lua.lua_pop(m_State, 1);
					continue;
				}

				var value = ConvertLuaValue(-1);
				dict[key] = value;

				Lua.lua_pop(m_State, 1);
			}

			return dict;
		}

		public void Dispose()
		{
			if (m_Disposed)
				return;

			// Note: Don't remove search paths here - they're a global shared registry
			// that may be used by other code even after this VM is disposed.

			foreach (var kvp in m_ScriptRefs)
				Lua.luaL_unref(m_State, Lua.LUA_REGISTRYINDEX, kvp.Value);

			foreach (var kvp in m_StateRefs)
				Lua.luaL_unref(m_State, Lua.LUA_REGISTRYINDEX, kvp.Value);

			m_ScriptRefs.Clear();
			m_StateRefs.Clear();
			m_BridgeRegistrations.Clear();

			if (m_State.IsNotNull)
			{
				Lua.lua_close(m_State);
				m_State = default;
			}

			if (Instance == this)
				Instance = null;

			m_Disposed = true;
		}
	}
}
