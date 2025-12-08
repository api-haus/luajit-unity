namespace LuaECS.Core
{
	using System;
	using System.IO;
	using Unity.Collections;
	using Unity.Entities;
	using UnityEngine;
#if UNITY_EDITOR
	using UnityEditor;
#endif

	public static class LuaScriptPathUtility
	{
		public const string ScriptsFolderRelative = "Assets/StreamingAssets/lua/scripts";

		/// <summary>
		/// Computes a stable Hash128 for a script name using xxHash3.
		/// </summary>
		public static Unity.Entities.Hash128 HashScriptName(string scriptName)
		{
			var state = new xxHash3.StreamingState(isHash64: false);
			var bytes = System.Text.Encoding.UTF8.GetBytes(scriptName);
			unsafe
			{
				fixed (byte* ptr = bytes)
				{
					state.Update(ptr, bytes.Length);
				}
			}
			var hash = state.DigestHash128();
			return new Unity.Entities.Hash128(hash.x, hash.y, hash.z, hash.w);
		}

		public const string LuaExtension = ".lua";

		static readonly string s_ScriptsFolderRelativeWithSlash = ScriptsFolderRelative + "/";
		static readonly string s_ScriptsFolderAbsolute = Path.Combine(
			Application.streamingAssetsPath,
			"lua",
			"scripts"
		);

		public static string ScriptsFolderAbsolute => s_ScriptsFolderAbsolute;

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

			if (trimmed.StartsWith(ScriptsFolderRelative, StringComparison.OrdinalIgnoreCase))
				trimmed = trimmed.Substring(ScriptsFolderRelative.Length);
			if (trimmed.StartsWith(s_ScriptsFolderRelativeWithSlash, StringComparison.OrdinalIgnoreCase))
				trimmed = trimmed.Substring(s_ScriptsFolderRelativeWithSlash.Length);
			trimmed = trimmed.TrimStart('/');

			if (trimmed.EndsWith(LuaExtension, StringComparison.OrdinalIgnoreCase))
				trimmed = trimmed[..^LuaExtension.Length];

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
			return Path.Combine(ScriptsFolderAbsolute, relativePath + LuaExtension);
		}

		public static bool ScriptExists(string scriptId)
		{
			var path = GetScriptFilePath(scriptId);
			return !string.IsNullOrEmpty(path) && File.Exists(path);
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
				normalized.StartsWith(s_ScriptsFolderRelativeWithSlash, StringComparison.OrdinalIgnoreCase)
			)
				normalized = normalized.Substring(s_ScriptsFolderRelativeWithSlash.Length);
			else if (normalized.StartsWith(ScriptsFolderRelative, StringComparison.OrdinalIgnoreCase))
				normalized = normalized.Substring(ScriptsFolderRelative.Length);
			else
			{
				error = $"Asset must be inside '{ScriptsFolderRelative}'.";
				return false;
			}

			if (!normalized.EndsWith(LuaExtension, StringComparison.OrdinalIgnoreCase))
			{
				error = "Only .lua files are supported.";
				return false;
			}

			var relativeWithoutExtension = normalized[..^LuaExtension.Length];
			return TryNormalizeScriptId(relativeWithoutExtension, out scriptId, out error);
		}

#if UNITY_EDITOR
		public static bool TryGetScriptId(
			UnityEngine.Object asset,
			out string scriptId,
			out string error
		)
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
			return $"{ScriptsFolderRelative}/{relativePath}{LuaExtension}";
		}
#endif
	}
}
