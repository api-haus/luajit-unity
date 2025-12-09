namespace LuaVM.Core
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using UnityEngine;

	/// <summary>
	/// Static registry for Lua script search paths.
	/// Allows registering multiple directories to search for scripts.
	/// </summary>
	public static class LuaScriptSearchPaths
	{
		static readonly List<string> s_searchPaths = new();
		static readonly object s_searchPathLock = new object();
		static bool s_initialized;
		static string s_defaultScriptsPath;

		/// <summary>
		/// Gets the default scripts path (StreamingAssets/lua/scripts).
		/// </summary>
		public static string DefaultScriptsPath
		{
			get
			{
				if (s_defaultScriptsPath == null)
				{
					s_defaultScriptsPath = Path.Combine(Application.streamingAssetsPath, "lua", "scripts");
				}
				return s_defaultScriptsPath;
			}
		}

		/// <summary>
		/// Initializes the search path registry with the default StreamingAssets path.
		/// Called automatically on first use.
		/// </summary>
		public static void Initialize()
		{
			lock (s_searchPathLock)
			{
				if (s_initialized)
					return;
				s_searchPaths.Clear();
				// Default path is always last (lowest priority)
				s_searchPaths.Add(DefaultScriptsPath);
				s_initialized = true;
			}
		}

		/// <summary>
		/// Adds a search path for script loading.
		/// </summary>
		/// <param name="absolutePath">Absolute path to a scripts directory.</param>
		/// <param name="priority">Priority (0 = highest, searched first). Default paths have lowest priority.</param>
		public static void AddSearchPath(string absolutePath, int priority = 0)
		{
			lock (s_searchPathLock)
			{
				if (!s_initialized)
					Initialize();
				if (string.IsNullOrEmpty(absolutePath))
					return;

				// Remove if exists (to allow re-prioritizing)
				s_searchPaths.Remove(absolutePath);

				// Insert at priority position (0 = highest, searches first)
				// Keep default StreamingAssets path at the end
				var index = Math.Min(priority, Math.Max(0, s_searchPaths.Count - 1));
				s_searchPaths.Insert(index, absolutePath);
			}
		}

		/// <summary>
		/// Removes a search path from the registry.
		/// Cannot remove the default StreamingAssets path.
		/// </summary>
		public static void RemoveSearchPath(string absolutePath)
		{
			lock (s_searchPathLock)
			{
				// Never remove the default StreamingAssets path
				if (absolutePath != DefaultScriptsPath)
					s_searchPaths.Remove(absolutePath);
			}
		}

		/// <summary>
		/// Clears all custom search paths, leaving only the default StreamingAssets path.
		/// </summary>
		public static void ClearSearchPaths()
		{
			lock (s_searchPathLock)
			{
				s_searchPaths.Clear();
				s_searchPaths.Add(DefaultScriptsPath);
			}
		}

		/// <summary>
		/// Gets a copy of the current search paths.
		/// </summary>
		public static IReadOnlyList<string> GetSearchPaths()
		{
			lock (s_searchPathLock)
			{
				if (!s_initialized)
					Initialize();
				return s_searchPaths.ToArray();
			}
		}

		/// <summary>
		/// Tries to find a script file across all search paths.
		/// </summary>
		/// <param name="relativePath">Relative script path (e.g., "player.lua" or "enemies/goblin.lua").</param>
		/// <param name="foundPath">Output: absolute path where script was found.</param>
		/// <param name="searchedBasePath">Output: base path where script was found.</param>
		/// <returns>True if script was found.</returns>
		public static bool TryFindScript(
			string relativePath,
			out string foundPath,
			out string searchedBasePath
		)
		{
			foundPath = string.Empty;
			searchedBasePath = string.Empty;

			if (string.IsNullOrEmpty(relativePath))
				return false;

			lock (s_searchPathLock)
			{
				if (!s_initialized)
					Initialize();

				foreach (var basePath in s_searchPaths)
				{
					var fullPath = Path.Combine(basePath, relativePath);
					if (File.Exists(fullPath))
					{
						foundPath = fullPath;
						searchedBasePath = basePath;
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Checks if a script exists in any search path.
		/// </summary>
		/// <param name="relativePath">Relative script path (e.g., "player.lua" or "enemies/goblin.lua").</param>
		/// <returns>True if script exists.</returns>
		public static bool ScriptExists(string relativePath)
		{
			return TryFindScript(relativePath, out _, out _);
		}

		/// <summary>
		/// Gets the full path to a script, or the expected default path if not found.
		/// </summary>
		/// <param name="relativePath">Relative script path.</param>
		/// <returns>Absolute path to the script.</returns>
		public static string GetScriptPath(string relativePath)
		{
			if (TryFindScript(relativePath, out var foundPath, out _))
				return foundPath;

			// Return expected path even if not found (for error messages)
			return Path.Combine(DefaultScriptsPath, relativePath);
		}
	}
}
