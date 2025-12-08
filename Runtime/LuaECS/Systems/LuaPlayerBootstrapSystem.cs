namespace LuaECS.Systems
{
	using LuaCharacters.ThirdPerson;
	using LuaECS.Core;
	using LuaNET.LuaJIT;
	using LuaVM.Core;
	using Unity.Entities;
	using Unity.Logging;

	[UpdateInGroup(typeof(InitializationSystemGroup))]
	[UpdateAfter(typeof(LuaScriptingSystem))]
	public partial class LuaPlayerBootstrapSystem : SystemBase
	{
		LuaVMManager m_VM;
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
			m_VM = LuaVMManager.GetOrCreate();
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

			if (inputActions?.InputActionAsset == null)
			{
				Log.Warning("[LuaPlayerBootstrap] InputActionAsset not set; skipping input initialization");
				return;
			}

			LuaECSBridge.InitializeInputSystem(inputActions.InputActionAsset);

			var actionCount = 0;
			foreach (var _ in inputActions.InputActionAsset)
				actionCount++;

			Log.Info("[LuaPlayerBootstrap] Input system initialized with {0} actions", actionCount);
		}

		protected override void OnUpdate()
		{
			if (m_PlayerSpawned)
				return;

			if (m_VM == null || !m_VM.IsValid)
				return;

			InvokeOnPlayerConnected(1);
			m_PlayerSpawned = true;
			Log.Info("[LuaPlayerBootstrap] Player 1 connected");
		}

		void InvokeOnPlayerConnected(int playerId)
		{
			if (!m_VM.IsValid)
				return;

			var L = m_VM.State;

			Lua.lua_getglobal(L, "OnPlayerConnected");
			if (Lua.lua_isfunction(L, -1) == 0)
			{
				Lua.lua_pop(L, 1);
				return;
			}

			Lua.lua_pushinteger(L, playerId);

			var result = Lua.lua_pcall(L, 1, 0, 0);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(L, -1);
				Log.Error("[LuaPlayerBootstrap] OnPlayerConnected error: {0}", error);
				Lua.lua_pop(L, 1);
			}
		}
	}
}
