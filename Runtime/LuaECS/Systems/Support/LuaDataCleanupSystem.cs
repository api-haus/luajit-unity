namespace LuaECS.Systems.Support
{
	using Components;
	using Core;
	using LuaVM.Core;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;

	/// <summary>
	/// Scrubs Lua-side data tables when entities with Lua-defined components are destroyed.
	/// Triggers via ICleanupComponentData: LuaDataCleanup survives entity destruction,
	/// this system detects orphaned cleanup markers and removes the Lua data.
	/// </summary>
	[UpdateInGroup(typeof(InitializationSystemGroup))]
	[UpdateBefore(typeof(LuaScriptCleanupSystem))]
	public partial class LuaDataCleanupSystem : SystemBase
	{
		LuaVMManager m_Vm;
		EntityQuery m_CleanupQuery;

		protected override void OnCreate()
		{
			m_CleanupQuery = GetEntityQuery(
				ComponentType.ReadOnly<LuaDataCleanup>(),
				ComponentType.Exclude<LuaEntityId>()
			);
		}

		protected override void OnStartRunning()
		{
			m_Vm = LuaVMManager.Instance ?? LuaVMManager.GetOrCreate();
		}

		protected override void OnUpdate()
		{
			if (m_CleanupQuery.IsEmptyIgnoreFilter)
				return;

			if (m_Vm == null || !m_Vm.IsValid)
			{
				if (LuaVMManager.Instance != null && LuaVMManager.Instance.IsValid)
					m_Vm = LuaVMManager.Instance;
				else
					return;
			}

			var entities = m_CleanupQuery.ToEntityArray(Allocator.Temp);
			var cleanups = m_CleanupQuery.ToComponentDataArray<LuaDataCleanup>(Allocator.Temp);
			var l = m_Vm.State;

			for (var i = 0; i < entities.Length; i++)
			{
				var entityId = cleanups[i].entityId;
				var componentNames = LuaComponentStore.GetEntityComponents(entityId);

				if (componentNames != null && componentNames.Count > 0)
				{
					LuaComponentStore.ScrubLuaData(l, entityId, componentNames);
					Log.Verbose(
						"[LuaDataCleanup] Scrubbed {0} Lua components for entity {1}",
						componentNames.Count,
						entityId
					);
				}

				LuaComponentStore.CleanupEntity(entityId);
				EntityManager.RemoveComponent<LuaDataCleanup>(entities[i]);
			}

			entities.Dispose();
			cleanups.Dispose();
		}
	}
}
