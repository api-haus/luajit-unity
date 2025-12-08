namespace LuaECS.Core
{
	using System;
	using AOT;
	using LuaECS.Components;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics;
	using Unity.Transforms;

	public static partial class LuaECSBridge
	{
		internal static void RegisterCommandFunctions(lua_State L)
		{
			RegisterFunction(L, "move_toward", ECS_MoveToward);
			RegisterFunction(L, "emit_command", ECS_EmitCommand);
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstCompile]
		static int ECS_MoveToward(lua_State L)
		{
			var entityIndex = (int)Lua.lua_tointeger(L, 1);
			var entity = GetEntityFromIdBurst(entityIndex);

			if (entity == Entity.Null)
				return 0;

			float3 targetPos;
			if (Lua.lua_istable(L, 2) != 0)
			{
				targetPos = TableToFloat3Burst(L, 2);
			}
			else if (Lua.lua_isnumber(L, 2) != 0)
			{
				var targetIndex = (int)Lua.lua_tointeger(L, 2);
				var targetEntity = GetEntityFromIdBurst(targetIndex);

				if (!TryGetTransformBurst(targetEntity, out var targetTransform))
					return 0;

				targetPos = targetTransform.Position;
			}
			else
			{
				return 0;
			}

			var speed = (float)Lua.lua_tonumber(L, 3);

			AddCommandBurst(
				new LuaCommand
				{
					Target = entity,
					Type = LuaCommandType.MoveToward,
					Position = targetPos,
					FloatParam = speed,
				}
			);

			return 0;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int ECS_EmitCommand(lua_State L)
		{
			if (!s_Initialized)
				return 0;

			var entityIndex = (int)Lua.lua_tointeger(L, 1);
			var entity = GetEntityFromIdBurst(entityIndex);

			if (entity == Entity.Null)
				return 0;

			var cmdTypeStr = Lua.lua_tostring(L, 2);
			if (!Enum.TryParse<LuaCommandType>(cmdTypeStr, true, out var cmdType))
			{
				Log.Warning("[LuaECS] Unknown command type: {0}", cmdTypeStr);
				return 0;
			}

			var cmd = new LuaCommand { Target = entity, Type = cmdType };

			if (Lua.lua_gettop(L) >= 3 && Lua.lua_istable(L, 3) != 0)
			{
				Lua.lua_getfield(L, 3, "position");
				if (Lua.lua_istable(L, -1) != 0)
					cmd.Position = TableToFloat3Burst(L, -1);
				Lua.lua_pop(L, 1);

				Lua.lua_getfield(L, 3, "float");
				if (Lua.lua_isnumber(L, -1) != 0)
					cmd.FloatParam = (float)Lua.lua_tonumber(L, -1);
				Lua.lua_pop(L, 1);

				Lua.lua_getfield(L, 3, "int");
				if (Lua.lua_isnumber(L, -1) != 0)
					cmd.IntParam = (int)Lua.lua_tointeger(L, -1);
				Lua.lua_pop(L, 1);

				Lua.lua_getfield(L, 3, "target");
				if (Lua.lua_isnumber(L, -1) != 0)
				{
					var targetIdx = (int)Lua.lua_tointeger(L, -1);
					cmd.SecondaryTarget = GetEntityFromIdBurst(targetIdx);
				}
				Lua.lua_pop(L, 1);
			}

			s_PendingCommands.Add(cmd);
			return 0;
		}
	}
}
