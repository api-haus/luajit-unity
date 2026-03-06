namespace LuaECS.Core
{
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Mathematics;

	public static class LuaSystemBridge
	{
		struct DeltaTimeMarker { }

		struct ElapsedTimeMarker { }

		static readonly SharedStatic<float> s_deltaTime =
			SharedStatic<float>.GetOrCreate<DeltaTimeMarker>();

		static readonly SharedStatic<double> s_elapsedTime =
			SharedStatic<double>.GetOrCreate<ElapsedTimeMarker>();

		static Unity.Mathematics.Random s_random;

		public static void UpdateContext(float deltaTime, double elapsedTime)
		{
			s_deltaTime.Data = deltaTime;
			s_elapsedTime.Data = elapsedTime;
		}

		public static void Register(lua_State l)
		{
			s_random = new Unity.Mathematics.Random(
				(uint)System.Environment.TickCount | 1u
			);

			Lua.lua_newtable(l);

			Lua.lua_pushcfunction(l, System_DeltaTime);
			Lua.lua_setfield(l, -2, "delta_time");

			Lua.lua_pushcfunction(l, System_Time);
			Lua.lua_setfield(l, -2, "time");

			Lua.lua_pushcfunction(l, System_Random);
			Lua.lua_setfield(l, -2, "random");

			Lua.lua_pushcfunction(l, System_RandomInt);
			Lua.lua_setfield(l, -2, "random_int");

			Lua.lua_setglobal(l, "system");
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int System_DeltaTime(lua_State l)
		{
			Lua.lua_pushnumber(l, s_deltaTime.Data);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int System_Time(lua_State l)
		{
			Lua.lua_pushnumber(l, s_elapsedTime.Data);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int System_Random(lua_State l)
		{
			var min = (float)Lua.lua_tonumber(l, 1);
			var max = (float)Lua.lua_tonumber(l, 2);
			var value = s_random.NextFloat(min, max);
			Lua.lua_pushnumber(l, value);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int System_RandomInt(lua_State l)
		{
			var min = (int)Lua.lua_tointeger(l, 1);
			var max = (int)Lua.lua_tointeger(l, 2);
			var value = s_random.NextInt(min, max + 1);
			Lua.lua_pushinteger(l, value);
			return 1;
		}
	}
}
