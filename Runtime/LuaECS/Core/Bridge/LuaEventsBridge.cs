namespace LuaECS.Core
{
	using AOT;
	using Components;
	using LuaNET.LuaJIT;
	using Unity.Collections;
	using Unity.Entities;

	/// <summary>
	/// Bridge functions for cross-entity event operations.
	/// Lua API: events.send_attack()
	/// </summary>
	public static partial class LuaECSBridge
	{
		internal static void RegisterEventsFunctions(lua_State l)
		{
			Lua.lua_newtable(l);

			RegisterFunction(l, "send_attack", Events_SendAttack);

			Lua.lua_setglobal(l, "events");
		}

		/// <summary>
		/// Send an attack event to a target entity.
		/// events.send_attack(source, target, damage)
		/// </summary>
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Events_SendAttack(lua_State l)
		{
			if (!s_initialized)
				return 0;

			var sourceId = (int)Lua.lua_tointeger(l, 1);
			var targetId = (int)Lua.lua_tointeger(l, 2);
			var damage = (int)Lua.lua_tointeger(l, 3);

			var source = GetEntityFromIdBurst(sourceId);
			var target = GetEntityFromIdBurst(targetId);

			if (target == Entity.Null)
				return 0;

			ref var ctx = ref s_burstContext.Data;
			if (!ctx.isValid)
				return 0;

			// Add event to target's event buffer via ECB
			FixedString32Bytes eventName = "on_attacked";
			var evt = new LuaEvent
			{
				eventName = eventName,
				source = source,
				target = target,
				intParam = damage,
			};
			ctx.ecb.AppendToBuffer(target, evt);

			return 0;
		}
	}
}
