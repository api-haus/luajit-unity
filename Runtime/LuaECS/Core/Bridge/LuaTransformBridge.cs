namespace LuaECS.Core
{
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Mathematics;

	public static partial class LuaECSBridge
	{
		struct TransformBridgeMarker { }

		static readonly SharedStatic<TransformFieldNames> s_fieldNames =
			SharedStatic<TransformFieldNames>.GetOrCreate<TransformBridgeMarker, TransformFieldNames>();

		struct TransformFieldNames
		{
			public unsafe fixed byte x[2];
			public unsafe fixed byte y[2];
			public unsafe fixed byte z[2];
			public bool initialized;
		}

		internal static void RegisterTransformFunctions(lua_State l)
		{
			InitializeFieldNames();
			RegisterFunction(l, "get_position", ECS_GetPosition);
			RegisterFunction(l, "set_position", ECS_SetPosition);
			RegisterFunction(l, "get_rotation", ECS_GetRotation);
		}

		static void InitializeFieldNames()
		{
			if (s_fieldNames.Data.initialized)
				return;

			unsafe
			{
				s_fieldNames.Data.x[0] = (byte)'x';
				s_fieldNames.Data.x[1] = 0;
				s_fieldNames.Data.y[0] = (byte)'y';
				s_fieldNames.Data.y[1] = 0;
				s_fieldNames.Data.z[0] = (byte)'z';
				s_fieldNames.Data.z[1] = 0;
			}
			s_fieldNames.Data.initialized = true;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_GetPosition(lua_State l)
		{
			var entityIndex = (int)Lua.lua_tointeger(l, 1);
			var entity = GetEntityFromIdBurst(entityIndex);

			if (!TryGetTransformBurst(entity, out var transform))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			PushFloat3AsTable(l, transform.Position);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_SetPosition(lua_State l)
		{
			var entityIndex = (int)Lua.lua_tointeger(l, 1);
			var entity = GetEntityFromIdBurst(entityIndex);

			if (!TryGetTransformBurst(entity, out var transform))
				return 0;

			var x = (float)Lua.lua_tonumber(l, 2);
			var y = (float)Lua.lua_tonumber(l, 3);
			var z = (float)Lua.lua_tonumber(l, 4);

			transform.Position = new float3(x, y, z);
			TrySetTransformBurst(entity, transform);

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_GetRotation(lua_State l)
		{
			var entityIndex = (int)Lua.lua_tointeger(l, 1);
			var entity = GetEntityFromIdBurst(entityIndex);

			if (!TryGetTransformBurst(entity, out var transform))
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			var euler = QuaternionToEulerBurst(transform.Rotation);
			PushFloat3AsTable(l, euler);
			return 1;
		}

		internal static void PushFloat3AsTable(lua_State l, float3 value)
		{
			Lua.lua_newtable(l);

			unsafe
			{
				fixed (byte* px = s_fieldNames.Data.x)
				fixed (byte* py = s_fieldNames.Data.y)
				fixed (byte* pz = s_fieldNames.Data.z)
				{
					Lua.lua_pushnumber(l, value.x);
					Lua.lua_setfield_ptr(l, -2, px);
					Lua.lua_pushnumber(l, value.y);
					Lua.lua_setfield_ptr(l, -2, py);
					Lua.lua_pushnumber(l, value.z);
					Lua.lua_setfield_ptr(l, -2, pz);
				}
			}
		}

		internal static float3 TableToFloat3Burst(lua_State l, int index)
		{
			var result = float3.zero;

			if (index < 0)
				index = Lua.lua_gettop(l) + index + 1;

			unsafe
			{
				fixed (byte* px = s_fieldNames.Data.x)
				fixed (byte* py = s_fieldNames.Data.y)
				fixed (byte* pz = s_fieldNames.Data.z)
				{
					Lua.lua_getfield_ptr(l, index, px);
					if (Lua.lua_isnumber(l, -1) != 0)
						result.x = (float)Lua.lua_tonumber(l, -1);
					Lua.lua_pop(l, 1);

					Lua.lua_getfield_ptr(l, index, py);
					if (Lua.lua_isnumber(l, -1) != 0)
						result.y = (float)Lua.lua_tonumber(l, -1);
					Lua.lua_pop(l, 1);

					Lua.lua_getfield_ptr(l, index, pz);
					if (Lua.lua_isnumber(l, -1) != 0)
						result.z = (float)Lua.lua_tonumber(l, -1);
					Lua.lua_pop(l, 1);
				}
			}

			return result;
		}

		static float3 QuaternionToEulerBurst(quaternion q)
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

		internal static unsafe bool TryReadLuaStringBurst(
			lua_State l,
			int idx,
			out FixedString64Bytes result
		)
		{
			result = default;
			ulong len = 0;
			var ptr = Lua.lua_tolstring_ptr(l, idx, ref len);
			if (ptr == null || len == 0)
				return false;

			if (len > 61)
				return false;

			for (var i = 0; i < (int)len; i++)
				result.Append((char)ptr[i]);

			return true;
		}
	}
}
