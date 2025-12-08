namespace LuaECS.Systems.Support
{
	using LuaECS.Components;
	using LuaVM.Core;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;

	/// <summary>
	/// Releases all Lua VM state tables tied to entities that were destroyed.
	/// Calls OnDestroy callbacks, releases VM state, and removes the LuaScript buffer.
	/// Runs in InitializationSystemGroup after LuaScriptFulfillmentSystem.
	/// </summary>
	[UpdateInGroup(typeof(InitializationSystemGroup))]
	[UpdateAfter(typeof(LuaScriptFulfillmentSystem))]
	public partial class LuaScriptCleanupSystem : SystemBase
	{
		LuaVMManager m_VM;

		protected override void OnStartRunning()
		{
			m_VM = LuaVMManager.Instance ?? LuaVMManager.GetOrCreate();
		}

		protected override void OnUpdate()
		{
			if (m_VM == null || !m_VM.IsValid)
			{
				if (LuaVMManager.Instance != null && LuaVMManager.Instance.IsValid)
					m_VM = LuaVMManager.Instance;
				else
					return;
			}

			var vm = m_VM;
			var entityManager = EntityManager;

			// Query entities with LuaScript buffer but no LuaEntityId (orphaned)
			var query = GetEntityQuery(
				ComponentType.Exclude<LuaEntityId>(),
				ComponentType.ReadWrite<LuaScript>()
			);

			if (query.IsEmptyIgnoreFilter)
				return;

			var entities = query.ToEntityArray(Allocator.Temp);

			if (entities.Length > 0)
			{
				Log.Verbose("[LuaCleanup] Found {0} entities for cleanup", entities.Length);
			}

			for (var i = 0; i < entities.Length; i++)
			{
				var scripts = entityManager.GetBuffer<LuaScript>(entities[i]);

				for (var j = 0; j < scripts.Length; j++)
				{
					var script = scripts[j];

					// Skip already disabled/cleaned scripts
					if (script.StateRef < 0 || script.Disabled)
						continue;

					var scriptName = script.ScriptName.ToString();

					vm.CallFunction(scriptName, "OnDestroy", script.EntityIndex, script.StateRef);

					Log.Verbose(
						"[LuaCleanup] Releasing state for {0}:{1} ref={2}",
						scriptName,
						script.EntityIndex,
						script.StateRef
					);
					vm.ReleaseEntityState(scriptName, script.EntityIndex, script.StateRef);
				}

				// DestroyEntity strips non-cleanup components but keeps cleanup components alive
				Log.Verbose("[LuaCleanup] Destroying entity {0}", entities[i].Index);
				entityManager.DestroyEntity(entities[i]);

				// Remove LuaScript buffer - entity is fully destroyed when no cleanup components remain
				entityManager.RemoveComponent<LuaScript>(entities[i]);
			}

			entities.Dispose();
		}
	}
}
