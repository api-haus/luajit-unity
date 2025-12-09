namespace LuaECS.Core
{
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.Logging;

	public static partial class LuaECSBridge
	{
		internal static void InitializeGlobalLog(lua_State l)
		{
			// Register internal log functions
			Lua.lua_newtable(l);
			RegisterFunction(l, "dispatch", ECS_LogDispatch);
			RegisterFunction(l, "traceback", ECS_Traceback);
			Lua.lua_setglobal(l, "_log_internal");

			const string logSetup =
				/**lua*/@"
local function format_message(message, ...)
	if select('#', ...) > 0 then
		local ok, formatted = pcall(string.format, message, ...)
		if ok then
			return formatted
		end
	end

	return tostring(message)
end

local function capture_stack_trace()
	local trace = debug.traceback('', 3) or ''
	return trace
end

local function dispatch_log(level, message, ...)
	local msg = format_message(message, ...)
	local stack_trace = capture_stack_trace()
	_log_internal.dispatch(level, msg, stack_trace)
end

local function make_logger(level)
	return function(message, ...)
		dispatch_log(level, message, ...)
	end
end

log = {
	debug = make_logger('debug'),
	info = make_logger('info'),
	warning = make_logger('warning'),
	error = make_logger('error'),
	trace = make_logger('trace'),
}
";

			var result = Lua.luaL_dostring(l, logSetup);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(l, -1) ?? "unknown error";
				Log.Error("[LuaECS] Failed to initialize Lua log helpers: {0}", error);
				Lua.lua_pop(l, 1);
			}
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int ECS_LogDispatch(lua_State l)
		{
			var level = Lua.lua_tostring(l, 1) ?? "info";
			var message = Lua.lua_tostring(l, 2) ?? "";
			var stackTrace = Lua.lua_tostring(l, 3) ?? "";

			switch (level)
			{
				case "debug":
					Log.Debug("[Lua] {0}\n{1}", message, FormatStackTrace(stackTrace));
					break;
				case "warning":
					Log.Warning("[Lua] {0}\n{1}", message, FormatStackTrace(stackTrace));
					break;
				case "error":
					Log.Error("[Lua] {0}\n{1}", message, FormatStackTrace(stackTrace));
					break;
				case "trace":
					Log.Error("[Lua TRACE] {0}\n{1}", message, FormatStackTrace(stackTrace));
					break;
				default:
					Log.Info("[Lua] {0}\n{1}", message, FormatStackTrace(stackTrace));
					break;
			}

			return 0;
		}

		static string FormatStackTrace(string stackTrace)
		{
			return stackTrace.Replace("\\n", "\n").Replace("\\t", "\t");
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int ECS_Traceback(lua_State l)
		{
			Lua.lua_getglobal(l, "debug");
			Lua.lua_getfield(l, -1, "traceback");
			Lua.lua_pushstring(l, "");
			Lua.lua_pushinteger(l, 2);
			Lua.lua_call(l, 2, 1);

			Lua.lua_insert(l, -2);
			Lua.lua_pop(l, 1);

			return 1;
		}
	}
}
