namespace LuaECS.Core
{
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.CharacterController;
	using Unity.Mathematics;

	public static partial class LuaECSBridge
	{
		internal static void RegisterMathFunctions(lua_State l)
		{
			// Extend Lua's built-in math table (don't replace it)
			Lua.lua_getglobal(l, "math");

			RegisterFunction(l, "cross", Math_Cross);
			RegisterFunction(l, "dot", Math_Dot);
			RegisterFunction(l, "normalize", Math_Normalize);
			RegisterFunction(l, "length", Math_Length);
			RegisterFunction(l, "length_sq", Math_LengthSq);
			RegisterFunction(l, "lerp", Math_Lerp);
			RegisterFunction(l, "clamp_length", Math_ClampLength);
			RegisterFunction(l, "project_on_plane", Math_ProjectOnPlane);
			RegisterFunction(l, "reorient_on_plane", Math_ReorientOnPlane);
			RegisterFunction(l, "distance", Math_Distance);

			Lua.lua_pop(l, 1);
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_Cross(lua_State l)
		{
			var a = TableToFloat3(l, 1);
			var b = TableToFloat3(l, 2);
			PushFloat3AsTable(l, math.cross(a, b));
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_Dot(lua_State l)
		{
			var a = TableToFloat3(l, 1);
			var b = TableToFloat3(l, 2);
			Lua.lua_pushnumber(l, math.dot(a, b));
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_Normalize(lua_State l)
		{
			var v = TableToFloat3(l, 1);
			PushFloat3AsTable(l, math.normalizesafe(v));
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_Length(lua_State l)
		{
			var v = TableToFloat3(l, 1);
			Lua.lua_pushnumber(l, math.length(v));
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_LengthSq(lua_State l)
		{
			var v = TableToFloat3(l, 1);
			Lua.lua_pushnumber(l, math.lengthsq(v));
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_Lerp(lua_State l)
		{
			var a = TableToFloat3(l, 1);
			var b = TableToFloat3(l, 2);
			var t = (float)Lua.lua_tonumber(l, 3);
			PushFloat3AsTable(l, math.lerp(a, b, t));
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_ClampLength(lua_State l)
		{
			var v = TableToFloat3(l, 1);
			var maxLen = (float)Lua.lua_tonumber(l, 2);
			PushFloat3AsTable(l, MathUtilities.ClampToMaxLength(v, maxLen));
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_ProjectOnPlane(lua_State l)
		{
			var v = TableToFloat3(l, 1);
			var n = TableToFloat3(l, 2);
			PushFloat3AsTable(l, MathUtilities.ProjectOnPlane(v, n));
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_ReorientOnPlane(lua_State l)
		{
			var v = TableToFloat3(l, 1);
			var n = TableToFloat3(l, 2);
			var dir = TableToFloat3(l, 3);
			PushFloat3AsTable(l, MathUtilities.ReorientVectorOnPlaneAlongDirection(v, n, dir));
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_Distance(lua_State l)
		{
			var a = TableToFloat3(l, 1);
			var b = TableToFloat3(l, 2);
			Lua.lua_pushnumber(l, math.distance(a, b));
			return 1;
		}
	}
}
