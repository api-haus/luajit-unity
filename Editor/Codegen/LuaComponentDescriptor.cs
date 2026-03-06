#if UNITY_EDITOR
namespace LuaGame.Editor.Codegen
{
	using System;
	using System.Collections.Generic;
	using System.Reflection;
	using System.Runtime.InteropServices;
	using Unity.Mathematics;

	public enum LuaFieldType
	{
		Float,
		Int,
		Bool,
		Float2,
		Float3,
		Float4,
		Quaternion,
		Unsupported,
	}

	public struct LuaFieldDescriptor
	{
		public string fieldName;
		public string luaName;
		public Type fieldType;
		public LuaFieldType luaFieldType;
		public int offset;
		public bool isReadOnly;
	}

	public static class LuaComponentDescriptor
	{
		public static List<LuaFieldDescriptor> Describe(Type componentType, bool readOnly = false)
		{
			var fields = new List<LuaFieldDescriptor>();

			foreach (
				var field in componentType.GetFields(
					BindingFlags.Public | BindingFlags.Instance
				)
			)
			{
				var luaType = MapFieldType(field.FieldType);
				if (luaType == LuaFieldType.Unsupported)
					continue;

				fields.Add(
					new LuaFieldDescriptor
					{
						fieldName = field.Name,
						luaName = ToSnakeCase(field.Name),
						fieldType = field.FieldType,
						luaFieldType = luaType,
						offset = (int)Marshal.OffsetOf(componentType, field.Name),
						isReadOnly = readOnly,
					}
				);
			}

			return fields;
		}

		public static string ToSnakeCase(string input)
		{
			if (string.IsNullOrEmpty(input))
				return input;

			var result = new System.Text.StringBuilder();
			for (var i = 0; i < input.Length; i++)
			{
				var c = input[i];
				if (char.IsUpper(c))
				{
					if (i > 0 && !char.IsUpper(input[i - 1]))
						result.Append('_');
					else if (
						i > 0
						&& i < input.Length - 1
						&& char.IsUpper(input[i - 1])
						&& char.IsLower(input[i + 1])
					)
						result.Append('_');
					result.Append(char.ToLower(c));
				}
				else
				{
					result.Append(c);
				}
			}
			return result.ToString();
		}

		public static LuaFieldType MapFieldType(Type type)
		{
			if (type == typeof(float))
				return LuaFieldType.Float;
			if (type == typeof(int))
				return LuaFieldType.Int;
			if (type == typeof(bool))
				return LuaFieldType.Bool;
			if (type == typeof(float2))
				return LuaFieldType.Float2;
			if (type == typeof(float3))
				return LuaFieldType.Float3;
			if (type == typeof(float4))
				return LuaFieldType.Float4;
			if (type == typeof(quaternion))
				return LuaFieldType.Quaternion;
			return LuaFieldType.Unsupported;
		}

		public static string GetPushCode(LuaFieldType type, string valueExpr)
		{
			return type switch
			{
				LuaFieldType.Float => $"Lua.lua_pushnumber(l, {valueExpr});",
				LuaFieldType.Int => $"Lua.lua_pushinteger(l, {valueExpr});",
				LuaFieldType.Bool => $"Lua.lua_pushboolean(l, {valueExpr} ? 1 : 0);",
				LuaFieldType.Float2 => $"PushFloat2(l, {valueExpr});",
				LuaFieldType.Float3 => $"PushFloat3(l, {valueExpr});",
				LuaFieldType.Float4 => $"PushFloat4(l, {valueExpr});",
				LuaFieldType.Quaternion => $"PushQuaternion(l, {valueExpr});",
				_ => $"Lua.lua_pushnil(l);",
			};
		}

		public static string GetReadCode(LuaFieldType type, int stackIndex)
		{
			return type switch
			{
				LuaFieldType.Float => $"(float)Lua.lua_tonumber(l, {stackIndex})",
				LuaFieldType.Int => $"(int)Lua.lua_tointeger(l, {stackIndex})",
				LuaFieldType.Bool => $"Lua.lua_toboolean(l, {stackIndex}) != 0",
				LuaFieldType.Float2 => $"ReadFloat2(l, {stackIndex})",
				LuaFieldType.Float3 => $"ReadFloat3(l, {stackIndex})",
				LuaFieldType.Float4 => $"ReadFloat4(l, {stackIndex})",
				LuaFieldType.Quaternion => $"ReadQuaternion(l, {stackIndex})",
				_ => "default",
			};
		}
	}
}
#endif
