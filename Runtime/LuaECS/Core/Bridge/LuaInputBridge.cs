namespace LuaECS.Core
{
	using System.Collections.Generic;
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.Mathematics;
	using UnityEngine.InputSystem;

	public static partial class LuaECSBridge
	{
		static Dictionary<string, InputAction> s_InputActions;
		static bool s_InputInitialized;

		internal static void RegisterInputFunctions(lua_State L)
		{
			Lua.lua_newtable(L);
			RegisterFunction(L, "read_value", Input_ReadValue);
			RegisterFunction(L, "was_pressed", Input_WasPressed);
			RegisterFunction(L, "is_held", Input_IsHeld);
			RegisterFunction(L, "was_released", Input_WasReleased);
			Lua.lua_setglobal(L, "input");
		}

		public static void InitializeInputSystem(InputActionAsset inputActionAsset)
		{
			if (s_InputActions == null)
			{
				s_InputActions = new Dictionary<string, InputAction>();
			}

			s_InputActions.Clear();

			if (inputActionAsset == null)
			{
				s_InputInitialized = false;
				return;
			}

			foreach (var action in inputActionAsset)
			{
				s_InputActions[action.name] = action;
				action.Enable();
			}

			s_InputInitialized = true;
		}

		static InputAction GetInputAction(lua_State L, int index)
		{
			if (!s_InputInitialized || s_InputActions == null)
				return null;

			ulong len = 0;
			unsafe
			{
				var ptr = Lua.lua_tolstring_ptr(L, index, ref len);
				if (ptr == null || len == 0)
					return null;

				var actionName = System.Text.Encoding.UTF8.GetString(ptr, (int)len);
				s_InputActions.TryGetValue(actionName, out var action);
				return action;
			}
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Input_ReadValue(lua_State L)
		{
			var action = GetInputAction(L, 1);
			if (action == null)
			{
				Lua.lua_pushnil(L);
				return 1;
			}

			if (action.expectedControlType == "Vector2")
			{
				var value = action.ReadValue<UnityEngine.Vector2>();
				PushFloat3AsTable(L, new float3(value.x, value.y, 0));
				return 1;
			}
			else if (action.expectedControlType == "Axis" || action.expectedControlType == "")
			{
				var value = action.ReadValue<float>();
				Lua.lua_pushnumber(L, value);
				return 1;
			}
			else
			{
				var isPressed = action.IsPressed();
				Lua.lua_pushboolean(L, isPressed ? 1 : 0);
				return 1;
			}
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Input_WasPressed(lua_State L)
		{
			var action = GetInputAction(L, 1);
			if (action == null)
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			var wasPressed = action.WasPressedThisFrame();
			Lua.lua_pushboolean(L, wasPressed ? 1 : 0);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Input_IsHeld(lua_State L)
		{
			var action = GetInputAction(L, 1);
			if (action == null)
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			var isPressed = action.IsPressed();
			Lua.lua_pushboolean(L, isPressed ? 1 : 0);
			return 1;
		}

		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		[BurstDiscard]
		static int Input_WasReleased(lua_State L)
		{
			var action = GetInputAction(L, 1);
			if (action == null)
			{
				Lua.lua_pushboolean(L, 0);
				return 1;
			}

			var wasReleased = action.WasReleasedThisFrame();
			Lua.lua_pushboolean(L, wasReleased ? 1 : 0);
			return 1;
		}
	}
}
