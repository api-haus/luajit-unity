namespace LuaECS.Systems.Tick
{
	using System.Collections.Generic;
	using Components;
	using LuaVM.Core;
	using Unity.Entities;
	using Unity.Logging;

	/// <summary>
	/// Base class for Lua tick systems. Processes scripts with a specific tick group.
	/// Derived classes specify which tick group they handle via GetTickGroup().
	/// </summary>
	public abstract partial class LuaTickSystemBase : SystemBase
	{
		protected LuaVMManager m_Vm;

		readonly List<(
			Entity entity,
			string scriptName,
			int entityIndex,
			int stateRef
		)> m_PendingTicks = new(64);

		/// <summary>
		/// Returns the tick group this system handles.
		/// </summary>
		protected abstract LuaTickGroup GetTickGroup();

		protected override void OnStartRunning()
		{
			m_Vm = LuaVMManager.Instance ?? LuaVMManager.GetOrCreate();
		}

		protected override void OnUpdate()
		{
			if (m_Vm == null || !m_Vm.IsValid)
			{
				if (LuaVMManager.Instance != null && LuaVMManager.Instance.IsValid)
					m_Vm = LuaVMManager.Instance;
				else
					return;
			}

			var tickGroup = GetTickGroup();

			// Use SystemAPI.Time for default worlds, fall back to Unity time for custom worlds
			var deltaTime = SystemAPI.Time.DeltaTime;
			if (deltaTime <= 0f)
				deltaTime = UnityEngine.Time.deltaTime;

			m_PendingTicks.Clear();

			// Collect scripts matching our tick group
			foreach (
				var (scripts, entity) in SystemAPI
					.Query<DynamicBuffer<LuaScript>>()
					.WithAll<LuaEntityId>()
					.WithEntityAccess()
			)
			{
				for (var i = 0; i < scripts.Length; i++)
				{
					var script = scripts[i];
					if (script.stateRef >= 0 && !script.disabled && script.tickGroup == tickGroup)
					{
						m_PendingTicks.Add(
							(entity, script.scriptName.ToString(), script.entityIndex, script.stateRef)
						);
					}
				}
			}

			// Process collected scripts
			foreach (var (entity, scriptName, entityIndex, stateRef) in m_PendingTicks)
			{
				if (!EntityManager.Exists(entity))
					continue;

				if (!EntityManager.HasComponent<LuaEntityId>(entity))
					continue;

				if (!m_Vm.ValidateStateRef(scriptName, entityIndex, stateRef))
				{
					Log.Warning(
						"[LuaTick:{0}] StateRef mismatch for {1} entity={2} - skipping",
						tickGroup,
						scriptName,
						entityIndex
					);
					continue;
				}

				m_Vm.CallTick(scriptName, entityIndex, stateRef, deltaTime);
			}
		}
	}
}
