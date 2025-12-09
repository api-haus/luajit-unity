namespace LuaECS.Core
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Text;
	using LuaVM.Core;
	using Unity.Collections;
	using UnityEngine;
	using Hash128 = Unity.Entities.Hash128;
	using Object = UnityEngine.Object;
#if UNITY_EDITOR
	using UnityEditor;
#endif

	public static class LuaScriptPathUtility
	{
		public const string SCRIPTS_FOLDER_RELATIVE = "Assets/StreamingAssets/lua/scripts";

		/// <summary>
		/// Computes a stable Hash128 for a script name using xxHash3.
		/// </summary>
		public static Hash128 HashScriptName(string scriptName)
		{
			var state = new xxHash3.StreamingState(isHash64: false);
			var bytes = Encoding.UTF8.GetBytes(scriptName);
			unsafe
			{
				fixed (byte* ptr = bytes)
				{
					state.Update(ptr, bytes.Length);
				}
			}
			var hash = state.DigestHash128();
			return new Hash128(hash.x, hash.y, hash.z, hash.w);
		}

		public const string LUA_EXTENSION = ".lua";

		static readonly string s_scriptsFolderRelativeWithSlash = SCRIPTS_FOLDER_RELATIVE + "/";

		public static string ScriptsFolderAbsolute => LuaScriptSearchPaths.DefaultScriptsPath;

		/// <summary>
		/// Initializes the search path registry with the default StreamingAssets path.
		/// Delegates to LuaScriptSearchPaths.
		/// </summary>
		public static void Initialize() => LuaScriptSearchPaths.Initialize();

		/// <summary>
		/// Adds a search path for script loading.
		/// Delegates to LuaScriptSearchPaths.
		/// </summary>
		public static void AddSearchPath(string absolutePath, int priority = 0) =>
			LuaScriptSearchPaths.AddSearchPath(absolutePath, priority);

		/// <summary>
		/// Removes a search path from the registry.
		/// Delegates to LuaScriptSearchPaths.
		/// </summary>
		public static void RemoveSearchPath(string absolutePath) =>
			LuaScriptSearchPaths.RemoveSearchPath(absolutePath);

		/// <summary>
		/// Clears all custom search paths, leaving only the default StreamingAssets path.
		/// Delegates to LuaScriptSearchPaths.
		/// </summary>
		public static void ClearSearchPaths() => LuaScriptSearchPaths.ClearSearchPaths();

		/// <summary>
		/// Gets a copy of the current search paths.
		/// Delegates to LuaScriptSearchPaths.
		/// </summary>
		public static IReadOnlyList<string> GetSearchPaths() => LuaScriptSearchPaths.GetSearchPaths();

		public static bool TryNormalizeScriptId(string input, out string scriptId, out string error)
		{
			scriptId = string.Empty;
			error = string.Empty;

			if (string.IsNullOrWhiteSpace(input))
			{
				error = "Script name cannot be empty.";
				return false;
			}

			var trimmed = input.Trim();
			trimmed = trimmed.Replace('\\', '/');

			if (trimmed.StartsWith(SCRIPTS_FOLDER_RELATIVE, StringComparison.OrdinalIgnoreCase))
				trimmed = trimmed.Substring(SCRIPTS_FOLDER_RELATIVE.Length);
			if (trimmed.StartsWith(s_scriptsFolderRelativeWithSlash, StringComparison.OrdinalIgnoreCase))
				trimmed = trimmed.Substring(s_scriptsFolderRelativeWithSlash.Length);
			trimmed = trimmed.TrimStart('/');

			if (trimmed.EndsWith(LUA_EXTENSION, StringComparison.OrdinalIgnoreCase))
				trimmed = trimmed[..^LUA_EXTENSION.Length];

			if (trimmed.Contains("..", StringComparison.Ordinal))
			{
				error = "Script name cannot navigate directories ('..' is not allowed).";
				return false;
			}

			for (var i = 0; i < trimmed.Length; i++)
			{
				var ch = trimmed[i];
				if (char.IsLetterOrDigit(ch))
					continue;

				if (ch is '_' or '-' or '/' or '.')
					continue;

				error = $"Character '{ch}' is not allowed in script names.";
				return false;
			}

			if (trimmed.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
			{
				error =
					$"Script names are limited to {FixedString64Bytes.UTF8MaxLengthInBytes} characters.";
				return false;
			}

			scriptId = trimmed;
			return true;
		}

		public static string NormalizeScriptId(string input)
		{
			return TryNormalizeScriptId(input, out var normalized, out _)
				? normalized
				: (input ?? string.Empty).Trim();
		}

		public static string GetScriptFilePath(string scriptId)
		{
			var normalized = NormalizeScriptId(scriptId);
			if (string.IsNullOrEmpty(normalized))
				return string.Empty;

			var relativePath = ScriptIdToRelativePath(normalized);
			var fileName = relativePath + LUA_EXTENSION;

			return LuaScriptSearchPaths.GetScriptPath(fileName);
		}

		public static bool ScriptExists(string scriptId)
		{
			var normalized = NormalizeScriptId(scriptId);
			if (string.IsNullOrEmpty(normalized))
				return false;

			var relativePath = ScriptIdToRelativePath(normalized);
			var fileName = relativePath + LUA_EXTENSION;

			return LuaScriptSearchPaths.ScriptExists(fileName);
		}

		/// <summary>
		/// Tries to find a script file across all search paths.
		/// </summary>
		/// <param name="scriptId">Script identifier.</param>
		/// <param name="foundPath">Output: absolute path where script was found.</param>
		/// <param name="searchedBasePath">Output: base path where script was found.</param>
		/// <returns>True if script was found.</returns>
		public static bool TryGetScriptFilePath(
			string scriptId,
			out string foundPath,
			out string searchedBasePath
		)
		{
			foundPath = string.Empty;
			searchedBasePath = string.Empty;

			var normalized = NormalizeScriptId(scriptId);
			if (string.IsNullOrEmpty(normalized))
				return false;

			var relativePath = ScriptIdToRelativePath(normalized);
			var fileName = relativePath + LUA_EXTENSION;

			return LuaScriptSearchPaths.TryFindScript(fileName, out foundPath, out searchedBasePath);
		}

		public static string ScriptIdToRelativePath(string scriptId)
		{
			if (string.IsNullOrEmpty(scriptId))
				return string.Empty;

			var normalized = scriptId.Replace('\\', '/');

			if (normalized.Contains('/'))
				return normalized.Trim('/');

			return normalized.Replace('.', '/').Trim('/');
		}

		public static bool TryGetScriptIdFromAssetPath(
			string assetPath,
			out string scriptId,
			out string error
		)
		{
			scriptId = string.Empty;
			error = string.Empty;

			if (string.IsNullOrEmpty(assetPath))
			{
				error = "Asset path is empty.";
				return false;
			}

			var normalized = assetPath.Replace('\\', '/');

			if (
				normalized.StartsWith(s_scriptsFolderRelativeWithSlash, StringComparison.OrdinalIgnoreCase)
			)
				normalized = normalized.Substring(s_scriptsFolderRelativeWithSlash.Length);
			else if (normalized.StartsWith(SCRIPTS_FOLDER_RELATIVE, StringComparison.OrdinalIgnoreCase))
				normalized = normalized.Substring(SCRIPTS_FOLDER_RELATIVE.Length);
			else
			{
				error = $"Asset must be inside '{SCRIPTS_FOLDER_RELATIVE}'.";
				return false;
			}

			if (!normalized.EndsWith(LUA_EXTENSION, StringComparison.OrdinalIgnoreCase))
			{
				error = "Only .lua files are supported.";
				return false;
			}

			var relativeWithoutExtension = normalized[..^LUA_EXTENSION.Length];
			return TryNormalizeScriptId(relativeWithoutExtension, out scriptId, out error);
		}

#if UNITY_EDITOR
		public static bool TryGetScriptId(Object asset, out string scriptId, out string error)
		{
			scriptId = string.Empty;
			error = string.Empty;

			if (asset == null)
			{
				error = "No asset selected.";
				return false;
			}

			var assetPath = AssetDatabase.GetAssetPath(asset);
			return TryGetScriptIdFromAssetPath(assetPath, out scriptId, out error);
		}

		public static string GetProjectRelativePath(string scriptId)
		{
			if (string.IsNullOrEmpty(scriptId))
				return string.Empty;

			var normalized = NormalizeScriptId(scriptId);
			if (string.IsNullOrEmpty(normalized))
				return string.Empty;

			var relativePath = ScriptIdToRelativePath(normalized);
			return $"{SCRIPTS_FOLDER_RELATIVE}/{relativePath}{LUA_EXTENSION}";
		}
#endif
	}
}
