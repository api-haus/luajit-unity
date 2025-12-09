namespace LuaECS.Core
{
	using System;
	using System.IO;
	using Components;

	/// <summary>
	/// Parses Lua script annotations from comment headers.
	/// Supported annotations:
	/// - @tick: variable|fixed|before_physics|after_physics|after_transform
	/// </summary>
	public static class LuaScriptAnnotationParser
	{
		const string TICK_ANNOTATION = "@tick:";
		const int MAX_HEADER_LINES = 20;

		/// <summary>
		/// Parsed script annotations.
		/// </summary>
		public struct ScriptAnnotations
		{
			public LuaTickGroup tickGroup;
			public bool hasTickAnnotation;
		}

		/// <summary>
		/// Parses annotations from script source code.
		/// Only scans the first MAX_HEADER_LINES lines for performance.
		/// </summary>
		public static ScriptAnnotations Parse(string source)
		{
			var result = new ScriptAnnotations
			{
				tickGroup = LuaTickGroup.Variable,
				hasTickAnnotation = false,
			};

			if (string.IsNullOrEmpty(source))
				return result;

			using var reader = new StringReader(source);
			var lineCount = 0;

			while (reader.ReadLine() is { } line && lineCount < MAX_HEADER_LINES)
			{
				lineCount++;
				var trimmed = line.Trim();

				// Only process comment lines
				if (!trimmed.StartsWith("--"))
					continue;

				// Remove comment prefix
				var content = trimmed[2..].Trim();

				// Check for @tick annotation
				if (content.StartsWith(TICK_ANNOTATION, StringComparison.OrdinalIgnoreCase))
				{
					var value = content[TICK_ANNOTATION.Length..].Trim().ToLowerInvariant();
					result.tickGroup = ParseTickGroup(value);
					result.hasTickAnnotation = true;
				}
			}

			return result;
		}

		/// <summary>
		/// Parses annotations from a script file.
		/// </summary>
		public static ScriptAnnotations ParseFile(string filePath)
		{
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				return new ScriptAnnotations
				{
					tickGroup = LuaTickGroup.Variable,
					hasTickAnnotation = false,
				};
			}

			// Read only enough lines for header parsing
			var lines = new System.Text.StringBuilder();
			using var reader = new StreamReader(filePath);
			var lineCount = 0;

			while (!reader.EndOfStream && lineCount < MAX_HEADER_LINES)
			{
				lines.AppendLine(reader.ReadLine());
				lineCount++;
			}

			return Parse(lines.ToString());
		}

		static LuaTickGroup ParseTickGroup(string value)
		{
			return value switch
			{
				"variable" => LuaTickGroup.Variable,
				"fixed" => LuaTickGroup.Fixed,
				"before_physics" => LuaTickGroup.BeforePhysics,
				"after_physics" => LuaTickGroup.AfterPhysics,
				"after_transform" => LuaTickGroup.AfterTransform,
				_ => LuaTickGroup.Variable,
			};
		}

		/// <summary>
		/// Returns the string representation of a tick group for use in annotations.
		/// </summary>
		public static string TickGroupToString(LuaTickGroup group)
		{
			return group switch
			{
				LuaTickGroup.Variable => "variable",
				LuaTickGroup.Fixed => "fixed",
				LuaTickGroup.BeforePhysics => "before_physics",
				LuaTickGroup.AfterPhysics => "after_physics",
				LuaTickGroup.AfterTransform => "after_transform",
				_ => "variable",
			};
		}
	}
}
