namespace LuaVM.Core
{
	using System;
	using LuaNET.LuaJIT;
	using Unity.Mathematics;

	/// <summary>
	/// Extension methods for working with lua_State.
	/// Provides convenient helpers for common Lua operations.
	/// </summary>
	public static class LuaStateExtensions
	{
		/// <summary>
		/// Push a float3 as a Lua table with x, y, z fields.
		/// </summary>
		public static void PushFloat3(this lua_State l, float3 value)
		{
			Lua.lua_newtable(l);
			Lua.lua_pushnumber(l, value.x);
			Lua.lua_setfield(l, -2, "x");
			Lua.lua_pushnumber(l, value.y);
			Lua.lua_setfield(l, -2, "y");
			Lua.lua_pushnumber(l, value.z);
			Lua.lua_setfield(l, -2, "z");
		}

		/// <summary>
		/// Read a float3 from a Lua table at the given stack index.
		/// </summary>
		public static float3 ToFloat3(this lua_State l, int index)
		{
			var result = float3.zero;

			if (index < 0)
				index = Lua.lua_gettop(l) + index + 1;

			Lua.lua_getfield(l, index, "x");
			if (Lua.lua_isnumber(l, -1) != 0)
				result.x = (float)Lua.lua_tonumber(l, -1);
			Lua.lua_pop(l, 1);

			Lua.lua_getfield(l, index, "y");
			if (Lua.lua_isnumber(l, -1) != 0)
				result.y = (float)Lua.lua_tonumber(l, -1);
			Lua.lua_pop(l, 1);

			Lua.lua_getfield(l, index, "z");
			if (Lua.lua_isnumber(l, -1) != 0)
				result.z = (float)Lua.lua_tonumber(l, -1);
			Lua.lua_pop(l, 1);

			return result;
		}

		/// <summary>
		/// Push a quaternion as a Lua table with x, y, z, w fields.
		/// </summary>
		public static void PushQuaternion(this lua_State l, quaternion value)
		{
			Lua.lua_newtable(l);
			Lua.lua_pushnumber(l, value.value.x);
			Lua.lua_setfield(l, -2, "x");
			Lua.lua_pushnumber(l, value.value.y);
			Lua.lua_setfield(l, -2, "y");
			Lua.lua_pushnumber(l, value.value.z);
			Lua.lua_setfield(l, -2, "z");
			Lua.lua_pushnumber(l, value.value.w);
			Lua.lua_setfield(l, -2, "w");
		}

		/// <summary>
		/// Read a quaternion from a Lua table at the given stack index.
		/// </summary>
		public static quaternion ToQuaternion(this lua_State l, int index)
		{
			var result = quaternion.identity;

			if (index < 0)
				index = Lua.lua_gettop(l) + index + 1;

			Lua.lua_getfield(l, index, "x");
			if (Lua.lua_isnumber(l, -1) != 0)
				result.value.x = (float)Lua.lua_tonumber(l, -1);
			Lua.lua_pop(l, 1);

			Lua.lua_getfield(l, index, "y");
			if (Lua.lua_isnumber(l, -1) != 0)
				result.value.y = (float)Lua.lua_tonumber(l, -1);
			Lua.lua_pop(l, 1);

			Lua.lua_getfield(l, index, "z");
			if (Lua.lua_isnumber(l, -1) != 0)
				result.value.z = (float)Lua.lua_tonumber(l, -1);
			Lua.lua_pop(l, 1);

			Lua.lua_getfield(l, index, "w");
			if (Lua.lua_isnumber(l, -1) != 0)
				result.value.w = (float)Lua.lua_tonumber(l, -1);
			Lua.lua_pop(l, 1);

			return result;
		}

		/// <summary>
		/// Convert quaternion to Euler angles (in degrees).
		/// </summary>
		public static float3 QuaternionToEuler(quaternion q)
		{
			float3 euler;

			var sinrCosp = 2 * ((q.value.w * q.value.x) + (q.value.y * q.value.z));
			var cosrCosp = 1 - (2 * ((q.value.x * q.value.x) + (q.value.y * q.value.y)));
			euler.x = math.atan2(sinrCosp, cosrCosp);

			var sinp = 2 * ((q.value.w * q.value.y) - (q.value.z * q.value.x));
			if (math.abs(sinp) >= 1)
				euler.y = math.sign(sinp) * math.PI / 2;
			else
				euler.y = math.asin(sinp);

			var sinyCosp = 2 * ((q.value.w * q.value.z) + (q.value.x * q.value.y));
			var cosyCosp = 1 - (2 * ((q.value.y * q.value.y) + (q.value.z * q.value.z)));
			euler.z = math.atan2(sinyCosp, cosyCosp);

			return math.degrees(euler);
		}

		/// <summary>
		/// Register a C# function to a table on the stack.
		/// </summary>
		public static void RegisterFunction(this lua_State l, string name, Lua.lua_CFunction func)
		{
			Lua.lua_pushcfunction(l, func);
			Lua.lua_setfield(l, -2, name);
		}

		/// <summary>
		/// Check if a value at the given index is a valid number and return it.
		/// </summary>
		public static bool TryGetNumber(this lua_State l, int index, out double value)
		{
			if (Lua.lua_isnumber(l, index) != 0)
			{
				value = Lua.lua_tonumber(l, index);
				return true;
			}
			value = 0;
			return false;
		}

		/// <summary>
		/// Check if a value at the given index is a valid integer and return it.
		/// </summary>
		public static bool TryGetInteger(this lua_State l, int index, out long value)
		{
			if (Lua.lua_isnumber(l, index) != 0)
			{
				value = Lua.lua_tointeger(l, index);
				return true;
			}
			value = 0;
			return false;
		}

		/// <summary>
		/// Check if a value at the given index is a valid string and return it.
		/// </summary>
		public static bool TryGetString(this lua_State l, int index, out string value)
		{
			if (Lua.lua_isstring(l, index) != 0)
			{
				value = Lua.lua_tostring(l, index);
				return true;
			}
			value = null;
			return false;
		}
	}
}
