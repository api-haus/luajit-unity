namespace LuaECS.Core
{
	using AOT;
	using Drawing;
	using LuaNET.LuaJIT;
	using Unity.Mathematics;
	using UnityEngine;

	public static partial class LuaECSBridge
	{
		static Color s_CurrentDrawColor = Color.white;
		static float s_CurrentDuration = 0f;

		internal static void RegisterDrawFunctions(lua_State L)
		{
			Lua.lua_newtable(L);

			RegisterFunction(L, "line", Draw_Line);
			RegisterFunction(L, "ray", Draw_Ray);
			RegisterFunction(L, "arrow", Draw_Arrow);

			RegisterFunction(L, "wire_sphere", Draw_WireSphere);
			RegisterFunction(L, "wire_box", Draw_WireBox);
			RegisterFunction(L, "wire_capsule", Draw_WireCapsule);
			RegisterFunction(L, "circle_xz", Draw_CircleXZ);

			RegisterFunction(L, "solid_box", Draw_SolidBox);
			RegisterFunction(L, "solid_circle", Draw_SolidCircle);

			RegisterFunction(L, "label_2d", Draw_Label2D);

			RegisterFunction(L, "set_color", Draw_SetColor);
			RegisterFunction(L, "with_duration", Draw_WithDuration);

			Lua.lua_setglobal(L, "draw");
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Line(lua_State L)
		{
			if (Lua.lua_gettop(L) < 2)
				return 0;

			var from = TableToFloat3(L, 1);
			var to = TableToFloat3(L, 2);

			if (s_CurrentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_CurrentDuration))
				{
					Draw.ingame.Line(from, to, s_CurrentDrawColor);
				}
			}
			else
			{
				Draw.ingame.Line(from, to, s_CurrentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Ray(lua_State L)
		{
			if (Lua.lua_gettop(L) < 2)
				return 0;

			var origin = TableToFloat3(L, 1);
			var direction = TableToFloat3(L, 2);

			if (s_CurrentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_CurrentDuration))
				{
					Draw.ingame.Ray(origin, direction, s_CurrentDrawColor);
				}
			}
			else
			{
				Draw.ingame.Ray(origin, direction, s_CurrentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Arrow(lua_State L)
		{
			if (Lua.lua_gettop(L) < 2)
				return 0;

			var from = TableToFloat3(L, 1);
			var to = TableToFloat3(L, 2);

			if (s_CurrentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_CurrentDuration))
				{
					Draw.ingame.Arrow(from, to, s_CurrentDrawColor);
				}
			}
			else
			{
				Draw.ingame.Arrow(from, to, s_CurrentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WireSphere(lua_State L)
		{
			if (Lua.lua_gettop(L) < 2)
				return 0;

			var center = TableToFloat3(L, 1);
			var radius = (float)Lua.lua_tonumber(L, 2);

			if (s_CurrentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_CurrentDuration))
				{
					Draw.ingame.WireSphere(center, radius, s_CurrentDrawColor);
				}
			}
			else
			{
				Draw.ingame.WireSphere(center, radius, s_CurrentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WireBox(lua_State L)
		{
			if (Lua.lua_gettop(L) < 2)
				return 0;

			var center = TableToFloat3(L, 1);
			var size = TableToFloat3(L, 2);

			if (s_CurrentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_CurrentDuration))
				{
					Draw.ingame.WireBox(center, size, s_CurrentDrawColor);
				}
			}
			else
			{
				Draw.ingame.WireBox(center, size, s_CurrentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WireCapsule(lua_State L)
		{
			if (Lua.lua_gettop(L) < 3)
				return 0;

			var start = TableToFloat3(L, 1);
			var end = TableToFloat3(L, 2);
			var radius = (float)Lua.lua_tonumber(L, 3);

			if (s_CurrentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_CurrentDuration))
				{
					Draw.ingame.WireCapsule(start, end, radius, s_CurrentDrawColor);
				}
			}
			else
			{
				Draw.ingame.WireCapsule(start, end, radius, s_CurrentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_CircleXZ(lua_State L)
		{
			if (Lua.lua_gettop(L) < 2)
				return 0;

			var center = TableToFloat3(L, 1);
			var radius = (float)Lua.lua_tonumber(L, 2);

			if (s_CurrentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_CurrentDuration))
				{
					Draw.ingame.xz.Circle(center, radius, s_CurrentDrawColor);
				}
			}
			else
			{
				Draw.ingame.xz.Circle(center, radius, s_CurrentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_SolidBox(lua_State L)
		{
			if (Lua.lua_gettop(L) < 2)
				return 0;

			var center = TableToFloat3(L, 1);
			var size = TableToFloat3(L, 2);

			if (s_CurrentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_CurrentDuration))
				{
					Draw.ingame.SolidBox(center, size, s_CurrentDrawColor);
				}
			}
			else
			{
				Draw.ingame.SolidBox(center, size, s_CurrentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_SolidCircle(lua_State L)
		{
			if (Lua.lua_gettop(L) < 3)
				return 0;

			var center = TableToFloat3(L, 1);
			var normal = TableToFloat3(L, 2);
			var radius = (float)Lua.lua_tonumber(L, 3);

			if (s_CurrentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_CurrentDuration))
				{
					Draw.ingame.SolidCircle(center, normal, radius, s_CurrentDrawColor);
				}
			}
			else
			{
				Draw.ingame.SolidCircle(center, normal, radius, s_CurrentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Label2D(lua_State L)
		{
			if (Lua.lua_gettop(L) < 2)
				return 0;

			var position = TableToFloat3(L, 1);

			ulong len = 0;
			unsafe
			{
				var ptr = Lua.lua_tolstring_ptr(L, 2, ref len);
				if (ptr == null || len == 0)
					return 0;

				var text = System.Text.Encoding.UTF8.GetString(ptr, (int)len);

				if (s_CurrentDuration > 0)
				{
					using (Draw.ingame.WithDuration(s_CurrentDuration))
					{
						Draw.ingame.Label2D(position, text, s_CurrentDrawColor);
					}
				}
				else
				{
					Draw.ingame.Label2D(position, text, s_CurrentDrawColor);
				}
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_SetColor(lua_State L)
		{
			var r = (float)Lua.lua_tonumber(L, 1);
			var g = (float)Lua.lua_tonumber(L, 2);
			var b = (float)Lua.lua_tonumber(L, 3);
			var a = Lua.lua_gettop(L) >= 4 ? (float)Lua.lua_tonumber(L, 4) : 1f;

			s_CurrentDrawColor = new Color(r, g, b, a);
			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WithDuration(lua_State L)
		{
			s_CurrentDuration = (float)Lua.lua_tonumber(L, 1);
			return 0;
		}
	}
}
