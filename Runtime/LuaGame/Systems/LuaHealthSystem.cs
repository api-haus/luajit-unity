namespace LuaGame.Systems
{
	using LuaECS.Components;
	using LuaECS.Systems;
	using Bridge;
	using Components;
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
				if (health.ValueRO.current <= 0 && !health.ValueRO.isDead)
				{
					health.ValueRW.isDead = true;

					events.Add(
						new LuaEvent
						{
							eventName = "OnDeath",
							source = entity,
							target = entity,
							intParam = 0,
							floatParam = health.ValueRO.max,
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
