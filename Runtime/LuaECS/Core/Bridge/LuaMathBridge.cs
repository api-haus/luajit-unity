namespace LuaECS.Core
{
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.Burst;
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
			RegisterFunction(l, "hsv_to_rgb", Math_HsvToRgb);
			RegisterFunction(l, "rgb_to_hsv", Math_RgbToHsv);
			RegisterFunction(l, "oklab_to_rgb", Math_OklabToRgb);
			RegisterFunction(l, "rgb_to_oklab", Math_RgbToOklab);

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

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int Math_HsvToRgb(lua_State l)
		{
			var h = (float)Lua.lua_tonumber(l, 1); // degrees [0..360]
			var s = (float)Lua.lua_tonumber(l, 2);
			var v = (float)Lua.lua_tonumber(l, 3);

			h = ((h % 360f) + 360f) % 360f;
			var c = v * s;
			var x = c * (1f - math.abs((h / 60f) % 2f - 1f));
			var m = v - c;

			float3 rgb;
			if (h < 60f) rgb = new float3(c, x, 0f);
			else if (h < 120f) rgb = new float3(x, c, 0f);
			else if (h < 180f) rgb = new float3(0f, c, x);
			else if (h < 240f) rgb = new float3(0f, x, c);
			else if (h < 300f) rgb = new float3(x, 0f, c);
			else rgb = new float3(c, 0f, x);

			PushFloat3AsTable(l, rgb + m);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_RgbToHsv(lua_State l)
		{
			var rgb = TableToFloat3(l, 1);
			var cMax = math.max(rgb.x, math.max(rgb.y, rgb.z));
			var cMin = math.min(rgb.x, math.min(rgb.y, rgb.z));
			var delta = cMax - cMin;

			float h = 0f;
			if (delta > 1e-6f)
			{
				if (cMax == rgb.x) h = 60f * (((rgb.y - rgb.z) / delta) % 6f);
				else if (cMax == rgb.y) h = 60f * ((rgb.z - rgb.x) / delta + 2f);
				else h = 60f * ((rgb.x - rgb.y) / delta + 4f);
			}
			if (h < 0f) h += 360f;

			var s = cMax > 1e-6f ? delta / cMax : 0f;

			Lua.lua_pushnumber(l, h);
			Lua.lua_pushnumber(l, s);
			Lua.lua_pushnumber(l, cMax);
			return 3;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_OklabToRgb(lua_State l)
		{
			var lab = TableToFloat3(l, 1);
			var lp = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
			var mp = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
			var sp = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;

			var ll = lp * lp * lp;
			var mm = mp * mp * mp;
			var ss = sp * sp * sp;

			var r = +4.0767416621f * ll - 3.3077115913f * mm + 0.2309699292f * ss;
			var g = -1.2684380046f * ll + 2.6097574011f * mm - 0.3413193965f * ss;
			var b = -0.0041960863f * ll - 0.7034186147f * mm + 1.7076147010f * ss;

			PushFloat3AsTable(l, new float3(r, g, b));
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_RgbToOklab(lua_State l)
		{
			var rgb = TableToFloat3(l, 1);

			var ll = 0.4122214708f * rgb.x + 0.5363325363f * rgb.y + 0.0514459929f * rgb.z;
			var mm = 0.2119034982f * rgb.x + 0.6806995451f * rgb.y + 0.1073969566f * rgb.z;
			var ss = 0.0883024619f * rgb.x + 0.2817188376f * rgb.y + 0.6299787005f * rgb.z;

			var lc = math.pow(math.max(ll, 0f), 1f / 3f);
			var mc = math.pow(math.max(mm, 0f), 1f / 3f);
			var sc = math.pow(math.max(ss, 0f), 1f / 3f);

			var L = 0.2104542553f * lc + 0.7936177850f * mc - 0.0040720468f * sc;
			var a = 1.9779984951f * lc - 2.4285922050f * mc + 0.4505937099f * sc;
			var bv = 0.0259040371f * lc + 0.7827717662f * mc - 0.8086757660f * sc;

			PushFloat3AsTable(l, new float3(L, a, bv));
			return 1;
		}
	}
}
