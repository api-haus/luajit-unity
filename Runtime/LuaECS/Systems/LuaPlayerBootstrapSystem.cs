namespace LuaECS.Systems
{
	using Core;
	using LuaCharacters.ThirdPerson;
	using LuaCharacters.ThirdPerson.Components;
	using LuaNET.LuaJIT;
	using LuaVM.Core;
	using Unity.Entities;
	using Unity.Logging;

	[UpdateInGroup(typeof(InitializationSystemGroup))]
	[UpdateAfter(typeof(LuaScriptingSystem))]
	public partial class LuaPlayerBootstrapSystem : SystemBase
	{
		LuaVMManager m_Vm;
		bool m_PlayerSpawned;
		EntityQuery m_BootstrapConfigQuery;

		protected override void OnCreate()
		{
			m_BootstrapConfigQuery = GetEntityQuery(typeof(LuaPlayerBootstrapConfig));
			RequireForUpdate<LuaScriptingSystemSingleton>();
			RequireForUpdate<LuaPlayerBootstrapConfig>();
			m_PlayerSpawned = false;
		}

		protected override void OnStartRunning()
		{
			m_Vm = LuaVMManager.GetOrCreate();
			TryInitializeInputSystem();
		}

		void TryInitializeInputSystem()
		{
			if (m_BootstrapConfigQuery.CalculateEntityCount() == 0)
				return;

			var configEntity = m_BootstrapConfigQuery.GetSingletonEntity();
			if (!EntityManager.HasComponent<ThirdPersonPlayerInputActions>(configEntity))
			{
				Log.Warning(
					"[LuaPlayerBootstrap] Bootstrap config present but missing ThirdPersonPlayerInputActions component"
				);
				return;
			}

			var inputActions = EntityManager.GetComponentObject<ThirdPersonPlayerInputActions>(
				configEntity
			);

			if (inputActions?.inputActionAsset == null)
			{
				Log.Warning("[LuaPlayerBootstrap] InputActionAsset not set; skipping input initialization");
				return;
			}

			LuaECSBridge.InitializeInputSystem(inputActions.inputActionAsset);

			var actionCount = 0;
			foreach (var _ in inputActions.inputActionAsset)
				actionCount++;

			Log.Info("[LuaPlayerBootstrap] Input system initialized with {0} actions", actionCount);
		}

		protected override void OnUpdate()
		{
			if (m_PlayerSpawned)
				return;

			if (m_Vm == null || !m_Vm.IsValid)
				return;

			InvokeOnPlayerConnected(1);
			m_PlayerSpawned = true;
			Log.Info("[LuaPlayerBootstrap] Player 1 connected");
		}

		void InvokeOnPlayerConnected(int playerId)
		{
			if (!m_Vm.IsValid)
				return;

			var l = m_Vm.State;

			Lua.lua_getglobal(l, "OnPlayerConnected");
			if (Lua.lua_isfunction(l, -1) == 0)
			{
				Lua.lua_pop(l, 1);
				return;
			}

			Lua.lua_pushinteger(l, playerId);

			var result = Lua.lua_pcall(l, 1, 0, 0);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(l, -1);
				Log.Error("[LuaPlayerBootstrap] OnPlayerConnected error: {0}", error);
				Lua.lua_pop(l, 1);
			}
		}
	}
}
