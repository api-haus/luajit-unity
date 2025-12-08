namespace LuaVM.Core
{
	using System;
	using System.IO;
	using Unity.Collections;
	using UnityEngine;

	/// <summary>
	/// Source type for loaded Lua scripts.
	/// </summary>
	public enum LuaScriptSourceType : byte
	{
		None = 0,
		String = 1,
		TextAsset = 2,
		StreamingAssets = 3,
		FilePath = 4,
	}

	/// <summary>
	/// Burst-compatible result structure for script loading operations.
	/// Contains script metadata and file path for file-based sources.
	/// </summary>
	public struct LuaScriptLoadResult
	{
		/// <summary>
		/// Unique identifier for the script (used as key in VM).
		/// </summary>
		public FixedString64Bytes ScriptId;

		/// <summary>
		/// Absolute file path for file-based scripts.
		/// </summary>
		public FixedString512Bytes FilePath;

		/// <summary>
		/// How the script was loaded.
		/// </summary>
		public LuaScriptSourceType SourceType;

		/// <summary>
		/// True if the script was loaded successfully.
		/// </summary>
		public bool IsValid;

		/// <summary>
		/// Error message if loading failed.
		/// </summary>
		public FixedString128Bytes Error;

		/// <summary>
		/// Creates a successful load result.
		/// </summary>
		public static LuaScriptLoadResult Success(
			FixedString64Bytes scriptId,
			LuaScriptSourceType sourceType,
			FixedString512Bytes filePath = default
		)
		{
			return new LuaScriptLoadResult
			{
				ScriptId = scriptId,
				FilePath = filePath,
				SourceType = sourceType,
				IsValid = true,
				Error = default,
			};
		}

		/// <summary>
		/// Creates a failed load result.
		/// </summary>
		public static LuaScriptLoadResult Failure(FixedString128Bytes error)
		{
			return new LuaScriptLoadResult
			{
				ScriptId = default,
				FilePath = default,
				SourceType = LuaScriptSourceType.None,
				IsValid = false,
				Error = error,
			};
		}
	}

	/// <summary>
	/// Centralized utility for loading Lua scripts from various sources.
	/// All methods return Burst-compatible structures.
	/// </summary>
	public static class LuaScriptLoader
	{
		static readonly string s_StreamingAssetsLuaPath = Path.Combine(
			Application.streamingAssetsPath,
			"lua"
		);

		static readonly string s_StreamingAssetsScriptsPath = Path.Combine(
			s_StreamingAssetsLuaPath,
			"scripts"
		);

		/// <summary>
		/// Validates a script ID for use with string-based loading.
		/// </summary>
		/// <param name="scriptId">Unique identifier for the script.</param>
		/// <returns>Load result with script ID metadata.</returns>
		public static LuaScriptLoadResult ValidateScriptId(string scriptId)
		{
			if (string.IsNullOrEmpty(scriptId))
				return LuaScriptLoadResult.Failure("Script ID cannot be empty");

			if (scriptId.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
				return LuaScriptLoadResult.Failure("Script ID too long (max 64 bytes)");

			return LuaScriptLoadResult.Success(
				new FixedString64Bytes(scriptId),
				LuaScriptSourceType.String
			);
		}

		/// <summary>
		/// Validates a TextAsset for script loading.
		/// </summary>
		/// <param name="asset">TextAsset containing Lua code.</param>
		/// <returns>Load result with script ID metadata.</returns>
		public static LuaScriptLoadResult ValidateTextAsset(TextAsset asset)
		{
			if (asset == null)
				return LuaScriptLoadResult.Failure("TextAsset is null");

			var scriptId = asset.name;
			if (string.IsNullOrEmpty(scriptId))
				return LuaScriptLoadResult.Failure("TextAsset name is empty");

			if (string.IsNullOrEmpty(asset.text))
				return LuaScriptLoadResult.Failure("TextAsset content is empty");

			if (scriptId.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
				return LuaScriptLoadResult.Failure("Script ID too long (max 64 bytes)");

			return LuaScriptLoadResult.Success(
				new FixedString64Bytes(scriptId),
				LuaScriptSourceType.TextAsset
			);
		}

		/// <summary>
		/// Validates a TextAsset for script loading with custom script ID.
		/// </summary>
		/// <param name="scriptId">Custom script identifier.</param>
		/// <param name="asset">TextAsset containing Lua code.</param>
		/// <returns>Load result with script ID metadata.</returns>
		public static LuaScriptLoadResult ValidateTextAsset(string scriptId, TextAsset asset)
		{
			if (string.IsNullOrEmpty(scriptId))
				return LuaScriptLoadResult.Failure("Script ID cannot be empty");

			if (asset == null)
				return LuaScriptLoadResult.Failure("TextAsset is null");

			if (string.IsNullOrEmpty(asset.text))
				return LuaScriptLoadResult.Failure("TextAsset content is empty");

			if (scriptId.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
				return LuaScriptLoadResult.Failure("Script ID too long (max 64 bytes)");

			return LuaScriptLoadResult.Success(
				new FixedString64Bytes(scriptId),
				LuaScriptSourceType.TextAsset
			);
		}

		/// <summary>
		/// Load a script from StreamingAssets/lua/scripts.
		/// </summary>
		/// <param name="relativePath">Path relative to StreamingAssets/lua/scripts (e.g., "player" or "enemies/goblin").</param>
		/// <returns>Load result with file path reference.</returns>
		public static LuaScriptLoadResult FromStreamingAssets(string relativePath)
		{
			if (string.IsNullOrEmpty(relativePath))
				return LuaScriptLoadResult.Failure("Relative path cannot be empty");

			var normalized = NormalizePath(relativePath);
			if (normalized.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
				return LuaScriptLoadResult.Failure("Script ID too long (max 64 bytes)");

			var filePath = ResolveStreamingAssetsPath(normalized);
			if (string.IsNullOrEmpty(filePath))
				return LuaScriptLoadResult.Failure("Failed to resolve path");

			if (!File.Exists(filePath))
				return LuaScriptLoadResult.Failure($"File not found: {normalized}");

			if (filePath.Length > FixedString512Bytes.UTF8MaxLengthInBytes)
				return LuaScriptLoadResult.Failure("File path too long (max 512 bytes)");

			return LuaScriptLoadResult.Success(
				new FixedString64Bytes(normalized),
				LuaScriptSourceType.StreamingAssets,
				filePath: new FixedString512Bytes(filePath)
			);
		}

		/// <summary>
		/// Load a script from an arbitrary file path.
		/// </summary>
		/// <param name="filePath">Absolute or relative file path.</param>
		/// <returns>Load result with file path reference.</returns>
		public static LuaScriptLoadResult FromFile(string filePath)
		{
			if (string.IsNullOrEmpty(filePath))
				return LuaScriptLoadResult.Failure("File path cannot be empty");

			var resolvedPath = ResolvePath(filePath);
			if (string.IsNullOrEmpty(resolvedPath))
				return LuaScriptLoadResult.Failure("Failed to resolve path");

			if (!File.Exists(resolvedPath))
				return LuaScriptLoadResult.Failure($"File not found: {filePath}");

			var scriptId = Path.GetFileNameWithoutExtension(resolvedPath);
			if (string.IsNullOrEmpty(scriptId))
				return LuaScriptLoadResult.Failure("Could not extract script ID from path");

			if (scriptId.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
				return LuaScriptLoadResult.Failure("Script ID too long (max 64 bytes)");

			if (resolvedPath.Length > FixedString512Bytes.UTF8MaxLengthInBytes)
				return LuaScriptLoadResult.Failure("File path too long (max 512 bytes)");

			return LuaScriptLoadResult.Success(
				new FixedString64Bytes(scriptId),
				LuaScriptSourceType.FilePath,
				filePath: new FixedString512Bytes(resolvedPath)
			);
		}

		/// <summary>
		/// Load a script from an arbitrary file path with custom script ID.
		/// </summary>
		/// <param name="scriptId">Custom script identifier.</param>
		/// <param name="filePath">Absolute or relative file path.</param>
		/// <returns>Load result with file path reference.</returns>
		public static LuaScriptLoadResult FromFile(string scriptId, string filePath)
		{
			if (string.IsNullOrEmpty(scriptId))
				return LuaScriptLoadResult.Failure("Script ID cannot be empty");

			if (string.IsNullOrEmpty(filePath))
				return LuaScriptLoadResult.Failure("File path cannot be empty");

			var resolvedPath = ResolvePath(filePath);
			if (string.IsNullOrEmpty(resolvedPath))
				return LuaScriptLoadResult.Failure("Failed to resolve path");

			if (!File.Exists(resolvedPath))
				return LuaScriptLoadResult.Failure($"File not found: {filePath}");

			if (scriptId.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
				return LuaScriptLoadResult.Failure("Script ID too long (max 64 bytes)");

			if (resolvedPath.Length > FixedString512Bytes.UTF8MaxLengthInBytes)
				return LuaScriptLoadResult.Failure("File path too long (max 512 bytes)");

			return LuaScriptLoadResult.Success(
				new FixedString64Bytes(scriptId),
				LuaScriptSourceType.FilePath,
				filePath: new FixedString512Bytes(resolvedPath)
			);
		}

		/// <summary>
		/// Reads the source code for a file-based load result.
		/// </summary>
		/// <param name="result">Load result with file path.</param>
		/// <param name="source">Output source code.</param>
		/// <returns>True if source was read successfully.</returns>
		public static bool TryReadSource(in LuaScriptLoadResult result, out string source)
		{
			source = null;

			if (!result.IsValid)
				return false;

			if (
				result.SourceType != LuaScriptSourceType.StreamingAssets
				&& result.SourceType != LuaScriptSourceType.FilePath
			)
				return false;

			var filePath = result.FilePath.ToString();
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
				return false;

			try
			{
				source = File.ReadAllText(filePath);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		static string NormalizePath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return string.Empty;

			var normalized = path.Replace('\\', '/').Trim('/');

			if (normalized.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
				normalized = normalized[..^4];

			return normalized;
		}

		static string ResolveStreamingAssetsPath(string normalizedPath)
		{
			if (string.IsNullOrEmpty(normalizedPath))
				return null;

			var withExtension = normalizedPath + ".lua";
			var fullPath = Path.Combine(s_StreamingAssetsScriptsPath, withExtension);

			if (File.Exists(fullPath))
				return fullPath;

			fullPath = Path.Combine(s_StreamingAssetsLuaPath, withExtension);
			if (File.Exists(fullPath))
				return fullPath;

			return Path.Combine(s_StreamingAssetsScriptsPath, withExtension);
		}

		static string ResolvePath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			if (Path.IsPathRooted(path))
				return path;

			var fromProject = Path.Combine(Application.dataPath, "..", path);
			if (File.Exists(fromProject))
				return Path.GetFullPath(fromProject);

			var fromAssets = Path.Combine(Application.dataPath, path);
			if (File.Exists(fromAssets))
				return Path.GetFullPath(fromAssets);

			return Path.GetFullPath(path);
		}
	}
}
