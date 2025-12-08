namespace LuaECS.Core
{
	using System.Text;
	using AOT;
	using Drawing;
	using LuaNET.LuaJIT;
	using UnityEngine;

	public static partial class LuaECSBridge
	{
		static Color s_currentDrawColor = Color.white;
		static float s_currentDuration;

		internal static void RegisterDrawFunctions(lua_State l)
		{
			Lua.lua_newtable(l);

			RegisterFunction(l, "line", Draw_Line);
			RegisterFunction(l, "ray", Draw_Ray);
			RegisterFunction(l, "arrow", Draw_Arrow);

			RegisterFunction(l, "wire_sphere", Draw_WireSphere);
			RegisterFunction(l, "wire_box", Draw_WireBox);
			RegisterFunction(l, "wire_capsule", Draw_WireCapsule);
			RegisterFunction(l, "circle_xz", Draw_CircleXZ);

			RegisterFunction(l, "solid_box", Draw_SolidBox);
			RegisterFunction(l, "solid_circle", Draw_SolidCircle);

			RegisterFunction(l, "label_2d", Draw_Label2D);

			RegisterFunction(l, "set_color", Draw_SetColor);
			RegisterFunction(l, "with_duration", Draw_WithDuration);

			Lua.lua_setglobal(l, "draw");
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Line(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var from = TableToFloat3(l, 1);
			var to = TableToFloat3(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.Line(from, to, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.Line(from, to, s_currentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Ray(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var origin = TableToFloat3(l, 1);
			var direction = TableToFloat3(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.Ray(origin, direction, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.Ray(origin, direction, s_currentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Arrow(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var from = TableToFloat3(l, 1);
			var to = TableToFloat3(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.Arrow(from, to, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.Arrow(from, to, s_currentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WireSphere(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var center = TableToFloat3(l, 1);
			var radius = (float)Lua.lua_tonumber(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.WireSphere(center, radius, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.WireSphere(center, radius, s_currentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WireBox(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var center = TableToFloat3(l, 1);
			var size = TableToFloat3(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.WireBox(center, size, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.WireBox(center, size, s_currentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WireCapsule(lua_State l)
		{
			if (Lua.lua_gettop(l) < 3)
				return 0;

			var start = TableToFloat3(l, 1);
			var end = TableToFloat3(l, 2);
			var radius = (float)Lua.lua_tonumber(l, 3);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.WireCapsule(start, end, radius, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.WireCapsule(start, end, radius, s_currentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_CircleXZ(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var center = TableToFloat3(l, 1);
			var radius = (float)Lua.lua_tonumber(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.xz.Circle(center, radius, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.xz.Circle(center, radius, s_currentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_SolidBox(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var center = TableToFloat3(l, 1);
			var size = TableToFloat3(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.SolidBox(center, size, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.SolidBox(center, size, s_currentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_SolidCircle(lua_State l)
		{
			if (Lua.lua_gettop(l) < 3)
				return 0;

			var center = TableToFloat3(l, 1);
			var normal = TableToFloat3(l, 2);
			var radius = (float)Lua.lua_tonumber(l, 3);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.SolidCircle(center, normal, radius, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.SolidCircle(center, normal, radius, s_currentDrawColor);
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Label2D(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var position = TableToFloat3(l, 1);

			ulong len = 0;
			unsafe
			{
				var ptr = Lua.lua_tolstring_ptr(l, 2, ref len);
				if (ptr == null || len == 0)
					return 0;

				var text = Encoding.UTF8.GetString(ptr, (int)len);

				if (s_currentDuration > 0)
				{
					using (Draw.ingame.WithDuration(s_currentDuration))
					{
						Draw.ingame.Label2D(position, text, s_currentDrawColor);
					}
				}
				else
				{
					Draw.ingame.Label2D(position, text, s_currentDrawColor);
				}
			}

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_SetColor(lua_State l)
		{
			var r = (float)Lua.lua_tonumber(l, 1);
			var g = (float)Lua.lua_tonumber(l, 2);
			var b = (float)Lua.lua_tonumber(l, 3);
			var a = Lua.lua_gettop(l) >= 4 ? (float)Lua.lua_tonumber(l, 4) : 1f;

			s_currentDrawColor = new Color(r, g, b, a);
			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WithDuration(lua_State l)
		{
			s_currentDuration = (float)Lua.lua_tonumber(l, 1);
			return 0;
		}
	}
}
