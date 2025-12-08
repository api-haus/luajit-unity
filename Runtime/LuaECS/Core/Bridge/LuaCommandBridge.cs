namespace LuaECS.Core
{
	using System;
	using AOT;
	using Components;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics;

	public static partial class LuaECSBridge
	{
		internal static void RegisterCommandFunctions(lua_State l)
		{
			RegisterFunction(l, "move_toward", ECS_MoveToward);
			RegisterFunction(l, "emit_command", ECS_EmitCommand);
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_MoveToward(lua_State l)
		{
			var entityIndex = (int)Lua.lua_tointeger(l, 1);
			var entity = GetEntityFromIdBurst(entityIndex);

			if (entity == Entity.Null)
				return 0;

			float3 targetPos;
			if (Lua.lua_istable(l, 2) != 0)
			{
				targetPos = TableToFloat3Burst(l, 2);
			}
			else if (Lua.lua_isnumber(l, 2) != 0)
			{
				var targetIndex = (int)Lua.lua_tointeger(l, 2);
				var targetEntity = GetEntityFromIdBurst(targetIndex);

				if (!TryGetTransformBurst(targetEntity, out var targetTransform))
					return 0;

				targetPos = targetTransform.Position;
			}
			else
			{
				return 0;
			}

			var speed = (float)Lua.lua_tonumber(l, 3);

			AddCommandBurst(
				new LuaCommand
				{
					target = entity,
					type = LuaCommandType.MOVE_TOWARD,
					position = targetPos,
					floatParam = speed,
				}
			);

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int ECS_EmitCommand(lua_State l)
		{
			if (!s_initialized)
				return 0;

			var entityIndex = (int)Lua.lua_tointeger(l, 1);
			var entity = GetEntityFromIdBurst(entityIndex);

			if (entity == Entity.Null)
				return 0;

			var cmdTypeStr = Lua.lua_tostring(l, 2);
			if (!Enum.TryParse<LuaCommandType>(cmdTypeStr, true, out var cmdType))
			{
				Log.Warning("[LuaECS] Unknown command type: {0}", cmdTypeStr);
				return 0;
			}

			var cmd = new LuaCommand { target = entity, type = cmdType };

			if (Lua.lua_gettop(l) >= 3 && Lua.lua_istable(l, 3) != 0)
			{
				Lua.lua_getfield(l, 3, "position");
				if (Lua.lua_istable(l, -1) != 0)
					cmd.position = TableToFloat3Burst(l, -1);
				Lua.lua_pop(l, 1);

				Lua.lua_getfield(l, 3, "float");
				if (Lua.lua_isnumber(l, -1) != 0)
					cmd.floatParam = (float)Lua.lua_tonumber(l, -1);
				Lua.lua_pop(l, 1);

				Lua.lua_getfield(l, 3, "int");
				if (Lua.lua_isnumber(l, -1) != 0)
					cmd.intParam = (int)Lua.lua_tointeger(l, -1);
				Lua.lua_pop(l, 1);

				Lua.lua_getfield(l, 3, "target");
				if (Lua.lua_isnumber(l, -1) != 0)
				{
					var targetIdx = (int)Lua.lua_tointeger(l, -1);
					cmd.secondaryTarget = GetEntityFromIdBurst(targetIdx);
				}
				Lua.lua_pop(l, 1);
			}

			s_pendingCommands.Add(cmd);
			return 0;
		}
	}
}
