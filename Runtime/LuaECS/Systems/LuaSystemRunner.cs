namespace LuaECS.Systems
{
	using System.Collections.Generic;
	using System.IO;
	using Core;
	using LuaNET.LuaJIT;
	using LuaVM.Core;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Transforms;
	using UnityEngine;

	public struct LuaSystemManifest : IComponentData
	{
		public bool initialized;
	}

	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateAfter(typeof(LuaScriptingSystem))]
	public partial class LuaSystemRunner : SystemBase
	{
		LuaVMManager m_Vm;
		readonly List<string> m_SystemNames = new();
		readonly Dictionary<string, int> m_SystemRefs = new();
		bool m_BridgesRegistered;

		ComponentLookup<LocalTransform> m_TransformLookup;
		EntityQuery m_SentinelQuery;

		protected override void OnCreate()
		{
			m_TransformLookup = GetComponentLookup<LocalTransform>();
			m_SentinelQuery = GetEntityQuery(ComponentType.ReadWrite<Components.LuaEntityId>());
		}

		protected override void OnStartRunning()
		{
			m_Vm = LuaVMManager.GetOrCreate();

			if (!m_BridgesRegistered)
			{
				m_Vm.RegisterBridgeNow(LuaECSBridge.RegisterFunctions);
				m_Vm.RegisterBridgeNow(LuaSystemBridge.Register);
				m_Vm.RegisterBridgeNow(LuaQueryBridge.Register);
				m_Vm.RegisterBridgeNow(LuaComponentRegistry.RegisterAllBridges);
				m_BridgesRegistered = true;
			}

			// Initialize bridge — LuaScriptingSystem may co-exist for per-entity model
			var scriptingSystem = World.GetOrCreateSystemManaged<LuaScriptingSystem>();
			LuaECSBridge.Initialize(World, scriptingSystem);
			LuaEntityRegistry.Initialize();
			LuaQueryBridge.Initialize(EntityManager);

			if (!SystemAPI.HasSingleton<LuaSystemManifest>())
			{
				var entity = EntityManager.CreateEntity();
				EntityManager.AddComponentData(entity, new LuaSystemManifest { initialized = false });
			}

			DiscoverAndLoadSystems();
		}

		protected override void OnDestroy()
		{
			LuaQueryBridge.Shutdown();
		}

		void DiscoverAndLoadSystems()
		{
			var systemsPath = Path.Combine(Application.streamingAssetsPath, "lua", "systems");
			if (!Directory.Exists(systemsPath))
			{
				Log.Warning("[LuaSystemRunner] No systems directory at {0}", systemsPath);
				return;
			}

			var files = Directory.GetFiles(systemsPath, "*.lua", SearchOption.AllDirectories);
			foreach (var file in files)
			{
				var systemName = Path.GetFileNameWithoutExtension(file);
				if (m_SystemRefs.ContainsKey(systemName))
					continue;

				LoadSystem(systemName, file);
			}

			ref var manifest = ref SystemAPI.GetSingletonRW<LuaSystemManifest>().ValueRW;
			manifest.initialized = true;
		}

		void LoadSystem(string systemName, string filePath)
		{
			var l = m_Vm.State;
			var normalizedPath = filePath.Replace("\\", "/");

			var setupCode =
				$@"
				local env = setmetatable({{}}, {{ __index = _G }})
				local chunk, err = loadfile('{normalizedPath}')
				if not chunk then
					error('Failed to load system {systemName}: ' .. tostring(err))
				end
				setfenv(chunk, env)
				chunk()
				return env
			";

			var result = Lua.luaL_dostring(l, setupCode);
			if (result != Lua.LUA_OK)
			{
				var error = Lua.lua_tostring(l, -1);
				Log.Error("[LuaSystemRunner] Failed to load system '{0}': {1}", systemName, error);
				Lua.lua_pop(l, 1);
				return;
			}

			if (Lua.lua_istable(l, -1) == 0)
			{
				Log.Error("[LuaSystemRunner] System '{0}' did not return a table", systemName);
				Lua.lua_pop(l, 1);
				return;
			}

			var systemRef = Lua.luaL_ref(l, Lua.LUA_REGISTRYINDEX);
			m_SystemRefs[systemName] = systemRef;
			m_SystemNames.Add(systemName);
			Log.Info("[LuaSystemRunner] Loaded system: {0}", systemName);
		}

		public void ReloadSystem(string systemName)
		{
			var l = m_Vm.State;
			if (m_SystemRefs.TryGetValue(systemName, out var oldRef))
			{
				Lua.luaL_unref(l, Lua.LUA_REGISTRYINDEX, oldRef);
				m_SystemRefs.Remove(systemName);
				m_SystemNames.Remove(systemName);
			}

			var systemsPath = Path.Combine(Application.streamingAssetsPath, "lua", "systems");
			var filePath = Path.Combine(systemsPath, systemName + ".lua");
			if (File.Exists(filePath))
			{
				LoadSystem(systemName, filePath);
				Log.Info("[LuaSystemRunner] Reloaded system: {0}", systemName);
			}
		}

		protected override void OnUpdate()
		{
			if (m_Vm == null || !m_Vm.IsValid)
				return;

			if (m_SystemNames.Count == 0)
				return;

			var deltaTime = SystemAPI.Time.DeltaTime;
			if (deltaTime <= 0f)
				deltaTime = UnityEngine.Time.deltaTime;

			var elapsedTime = SystemAPI.Time.ElapsedTime;

			// Register sentinel entities (LuaEntityId.value == 0) from baking
			RegisterSentinelEntities();

			EntityManager.CompleteDependencyBeforeRW<LocalTransform>();
			m_TransformLookup.Update(this);

			// Update bridge contexts
			LuaSystemBridge.UpdateContext(deltaTime, elapsedTime);

			// Update generated bridge lookups
			ref var sysState = ref CheckedStateRef;
			LuaComponentRegistry.UpdateAllLookups(ref sysState);

			// Update the ECS bridge context so transform lookups work
			var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
			var ecb = ecbSingleton.CreateCommandBuffer(World.Unmanaged);
			var scriptBufferLookup = GetBufferLookup<Components.LuaScript>(true);
			LuaECSBridge.UpdateBurstContext(ecb, deltaTime, m_TransformLookup, scriptBufferLookup);

			var l = m_Vm.State;
			foreach (var systemName in m_SystemNames)
			{
				if (!m_SystemRefs.TryGetValue(systemName, out var systemRef))
					continue;

				Lua.lua_rawgeti(l, Lua.LUA_REGISTRYINDEX, systemRef);
				Lua.lua_getfield(l, -1, "OnUpdate");

				if (Lua.lua_isfunction(l, -1) == 0)
				{
					Lua.lua_pop(l, 2);
					continue;
				}

				Lua.lua_pushnumber(l, deltaTime);

				var result = Lua.lua_pcall(l, 1, 0, 0);
				if (result != Lua.LUA_OK)
				{
					var error = Lua.lua_tostring(l, -1);
					Log.Error("[LuaSystemRunner] Error in {0}.OnUpdate: {1}", systemName, error);
					Lua.lua_pop(l, 1);
				}

				Lua.lua_pop(l, 1); // pop env table
			}
		}

		void RegisterSentinelEntities()
		{
			var entities = m_SentinelQuery.ToEntityArray(Allocator.Temp);
			var ids = m_SentinelQuery.ToComponentDataArray<Components.LuaEntityId>(Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				if (ids[i].value != 0)
					continue;

				// Assign a real ID
				var newId = LuaEntityRegistry.AllocateId();
				if (newId <= 0)
					continue;

				EntityManager.SetComponentData(entities[i], new Components.LuaEntityId { value = newId });
				LuaEntityRegistry.RegisterImmediate(entities[i], newId, EntityManager);
			}

			entities.Dispose();
			ids.Dispose();
		}
	}
}
