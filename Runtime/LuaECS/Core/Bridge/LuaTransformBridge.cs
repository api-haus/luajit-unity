namespace LuaECS.Core
{
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Mathematics;
	using Unity.Transforms;

	public static partial class LuaECSBridge
	{
		struct TransformBridgeMarker { }

		static readonly SharedStatic<TransformFieldNames> s_FieldNames =
			SharedStatic<TransformFieldNames>.GetOrCreate<TransformBridgeMarker, TransformFieldNames>();

		struct TransformFieldNames
		{
			public unsafe fixed byte X[2];
			public unsafe fixed byte Y[2];
			public unsafe fixed byte Z[2];
			public bool Initialized;
		}

		internal static void RegisterTransformFunctions(lua_State L)
		{
			InitializeFieldNames();
			RegisterFunction(L, "get_position", ECS_GetPosition);
			RegisterFunction(L, "set_position", ECS_SetPosition);
			RegisterFunction(L, "get_rotation", ECS_GetRotation);
		}

		static void InitializeFieldNames()
		{
			if (s_FieldNames.Data.Initialized)
				return;

			unsafe
			{
				s_FieldNames.Data.X[0] = (byte)'x';
				s_FieldNames.Data.X[1] = 0;
				s_FieldNames.Data.Y[0] = (byte)'y';
				s_FieldNames.Data.Y[1] = 0;
				s_FieldNames.Data.Z[0] = (byte)'z';
				s_FieldNames.Data.Z[1] = 0;
			}
			s_FieldNames.Data.Initialized = true;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_GetPosition(lua_State L)
		{
			var entityIndex = (int)Lua.lua_tointeger(L, 1);
			var entity = GetEntityFromIdBurst(entityIndex);

			if (!TryGetTransformBurst(entity, out var transform))
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			PushFloat3AsTable(L, transform.Position);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_SetPosition(lua_State L)
		{
			var entityIndex = (int)Lua.lua_tointeger(L, 1);
			var entity = GetEntityFromIdBurst(entityIndex);

			if (!TryGetTransformBurst(entity, out var transform))
				return 0;

			var x = (float)Lua.lua_tonumber(L, 2);
			var y = (float)Lua.lua_tonumber(L, 3);
			var z = (float)Lua.lua_tonumber(L, 4);

			transform.Position = new float3(x, y, z);
			TrySetTransformBurst(entity, transform);

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_GetRotation(lua_State L)
		{
			var entityIndex = (int)Lua.lua_tointeger(L, 1);
			var entity = GetEntityFromIdBurst(entityIndex);

			if (!TryGetTransformBurst(entity, out var transform))
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			var euler = QuaternionToEulerBurst(transform.Rotation);
			PushFloat3AsTable(L, euler);
			return 1;
		}

		internal static void PushFloat3AsTable(lua_State L, float3 value)
		{
			Lua.lua_newtable(L);

			unsafe
			{
				fixed (byte* px = s_FieldNames.Data.X)
				fixed (byte* py = s_FieldNames.Data.Y)
				fixed (byte* pz = s_FieldNames.Data.Z)
				{
					Lua.lua_pushnumber(L, value.x);
					Lua.lua_setfield_ptr(L, -2, px);
					Lua.lua_pushnumber(L, value.y);
					Lua.lua_setfield_ptr(L, -2, py);
					Lua.lua_pushnumber(L, value.z);
					Lua.lua_setfield_ptr(L, -2, pz);
				}
			}
		}

		internal static float3 TableToFloat3Burst(lua_State L, int index)
		{
			var result = float3.zero;

			if (index < 0)
				index = Lua.lua_gettop(L) + index + 1;

			unsafe
			{
				fixed (byte* px = s_FieldNames.Data.X)
				fixed (byte* py = s_FieldNames.Data.Y)
				fixed (byte* pz = s_FieldNames.Data.Z)
				{
					Lua.lua_getfield_ptr(L, index, px);
					if (Lua.lua_isnumber(L, -1) != 0)
						result.x = (float)Lua.lua_tonumber(L, -1);
					Lua.lua_pop(L, 1);

					Lua.lua_getfield_ptr(L, index, py);
					if (Lua.lua_isnumber(L, -1) != 0)
						result.y = (float)Lua.lua_tonumber(L, -1);
					Lua.lua_pop(L, 1);

					Lua.lua_getfield_ptr(L, index, pz);
					if (Lua.lua_isnumber(L, -1) != 0)
						result.z = (float)Lua.lua_tonumber(L, -1);
					Lua.lua_pop(L, 1);
				}
			}

			return result;
		}

		static float3 QuaternionToEulerBurst(quaternion q)
		{
			float3 euler;

			var sinr_cosp = 2 * (q.value.w * q.value.x + q.value.y * q.value.z);
			var cosr_cosp = 1 - 2 * (q.value.x * q.value.x + q.value.y * q.value.y);
			euler.x = math.atan2(sinr_cosp, cosr_cosp);

			var sinp = 2 * (q.value.w * q.value.y - q.value.z * q.value.x);
			if (math.abs(sinp) >= 1)
				euler.y = math.sign(sinp) * math.PI / 2;
			else
				euler.y = math.asin(sinp);

			var siny_cosp = 2 * (q.value.w * q.value.z + q.value.x * q.value.y);
			var cosy_cosp = 1 - 2 * (q.value.y * q.value.y + q.value.z * q.value.z);
			euler.z = math.atan2(siny_cosp, cosy_cosp);

			return math.degrees(euler);
		}

		internal static unsafe bool TryReadLuaStringBurst(
			lua_State L,
			int idx,
			out FixedString64Bytes result
		)
		{
			result = default;
			ulong len = 0;
			var ptr = Lua.lua_tolstring_ptr(L, idx, ref len);
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
