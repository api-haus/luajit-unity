namespace LuaECS.Core
{
	using System.Collections.Generic;
	using System.Text;
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Mathematics;
	using UnityEngine;
	using UnityEngine.InputSystem;

	public static partial class LuaECSBridge
	{
		static Dictionary<string, InputAction> s_inputActions;
		static bool s_inputInitialized;

		internal static void RegisterInputFunctions(lua_State l)
		{
			Lua.lua_newtable(l);
			RegisterFunction(l, "read_value", Input_ReadValue);
			RegisterFunction(l, "was_pressed", Input_WasPressed);
			RegisterFunction(l, "is_held", Input_IsHeld);
			RegisterFunction(l, "was_released", Input_WasReleased);
			Lua.lua_setglobal(l, "input");
		}

		public static void InitializeInputSystem(InputActionAsset inputActionAsset)
		{
			if (s_inputActions == null)
			{
				s_inputActions = new Dictionary<string, InputAction>();
			}

			s_inputActions.Clear();

			if (inputActionAsset == null)
			{
				s_inputInitialized = false;
				return;
			}

			foreach (var action in inputActionAsset)
			{
				s_inputActions[action.name] = action;
				action.Enable();
			}

			s_inputInitialized = true;
		}

		static InputAction GetInputAction(lua_State l, int index)
		{
			if (!s_inputInitialized || s_inputActions == null)
				return null;

			ulong len = 0;
			unsafe
			{
				var ptr = Lua.lua_tolstring_ptr(l, index, ref len);
				if (ptr == null || len == 0)
					return null;

				var actionName = Encoding.UTF8.GetString(ptr, (int)len);
				s_inputActions.TryGetValue(actionName, out var action);
				return action;
			}
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Input_ReadValue(lua_State l)
		{
			var action = GetInputAction(l, 1);
			if (action == null)
			{
				Lua.lua_pushnil(l);
				return 1;
			}

			if (action.expectedControlType == "Vector2")
			{
				var value = action.ReadValue<Vector2>();
				PushFloat3AsTable(l, new float3(value.x, value.y, 0));
				return 1;
			}

			if (action.expectedControlType == "Axis" || action.expectedControlType == "")
			{
				var value = action.ReadValue<float>();
				Lua.lua_pushnumber(l, value);
				return 1;
			}

			var isPressed = action.IsPressed();
			Lua.lua_pushboolean(l, isPressed ? 1 : 0);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Input_WasPressed(lua_State l)
		{
			var action = GetInputAction(l, 1);
			if (action == null)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var wasPressed = action.WasPressedThisFrame();
			Lua.lua_pushboolean(l, wasPressed ? 1 : 0);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Input_IsHeld(lua_State l)
		{
			var action = GetInputAction(l, 1);
			if (action == null)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var isPressed = action.IsPressed();
			Lua.lua_pushboolean(l, isPressed ? 1 : 0);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Input_WasReleased(lua_State l)
		{
			var action = GetInputAction(l, 1);
			if (action == null)
			{
				Lua.lua_pushboolean(l, 0);
				return 1;
			}

			var wasReleased = action.WasReleasedThisFrame();
			Lua.lua_pushboolean(l, wasReleased ? 1 : 0);
			return 1;
		}
	}
}
