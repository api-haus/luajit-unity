namespace LuaGame.Systems
{
	using LuaECS.Components;
	using LuaECS.Systems;
	using LuaGame.Bridge;
	using LuaGame.Components;
	using LuaVM.Core;
	using Unity.Entities;

	/// <summary>
	/// Processes health changes and triggers OnDeath events when entities die.
	/// Also maintains the health bridge context for Lua access.
	/// Runs BEFORE LuaScriptingSystem to ensure context is valid when scripts call health functions.
	/// </summary>
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateBefore(typeof(LuaScriptingSystem))]
	public partial class LuaHealthSystem : SystemBase
	{
		ComponentLookup<LuaHealth> m_HealthLookup;
		bool m_BridgeRegistered;

		protected override void OnCreate()
		{
			m_HealthLookup = GetComponentLookup<LuaHealth>();
		}

		protected override void OnStartRunning()
		{
			if (!m_BridgeRegistered)
			{
				var vm = LuaVMManager.Instance ?? LuaVMManager.GetOrCreate();
				if (vm != null && vm.IsValid)
				{
					vm.RegisterBridgeNow(LuaHealthBridge.RegisterFunctions);
					m_BridgeRegistered = true;
				}
			}
		}

		protected override void OnUpdate()
		{
			m_HealthLookup.Update(this);
			LuaHealthBridge.UpdateContext(m_HealthLookup);

			foreach (
				var (health, events, entity) in SystemAPI
					.Query<RefRW<LuaHealth>, DynamicBuffer<LuaEvent>>()
					.WithEntityAccess()
			)
			{
				if (health.ValueRO.Current <= 0 && !health.ValueRO.IsDead)
				{
					health.ValueRW.IsDead = true;

					events.Add(
						new LuaEvent
						{
							EventName = "OnDeath",
							Source = entity,
							Target = entity,
							IntParam = 0,
							FloatParam = health.ValueRO.Max,
						}
					);
				}
			}
		}

		protected override void OnDestroy()
		{
			LuaHealthBridge.ClearContext();
		}
	}
}
