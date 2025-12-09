namespace LuaGame.Systems
{
	using Bridge;
	using Components;
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaECS.Systems;
	using LuaVM.Core;
	using Unity.Entities;

	/// <summary>
	/// Decrements time-to-live and handles expiration.
	/// Fires OnExpire event and optionally destroys entities.
	/// Runs before damage systems to process expired entities first.
	/// </summary>
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateBefore(typeof(LuaHealthSystem))]
	public partial class LuaTimeToLiveSystem : SystemBase
	{
		EndSimulationEntityCommandBufferSystem m_ECBSystem;
		ComponentLookup<LuaTimeToLive> m_TTLLookup;
		bool m_BridgeRegistered;

		protected override void OnCreate()
		{
			m_ECBSystem = World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
			m_TTLLookup = GetComponentLookup<LuaTimeToLive>();
		}

		protected override void OnStartRunning()
		{
			if (!m_BridgeRegistered)
			{
				var vm = LuaVMManager.Instance ?? LuaVMManager.GetOrCreate();
				if (vm != null && vm.IsValid)
				{
					vm.RegisterBridgeNow(LuaTTLBridge.RegisterFunctions);
					m_BridgeRegistered = true;
				}
			}
		}

		/// <summary>
		/// Primes the TTL context before OnInit runs.
		/// Called by GameModeManager to enable ttl.set() during OnInit.
		/// </summary>
		public void PrimeContextForOnInit()
		{
			m_TTLLookup.Update(this);
			var ecb = m_ECBSystem.CreateCommandBuffer();
			LuaTTLBridge.UpdateContext(m_TTLLookup, ecb);
		}

		protected override void OnUpdate()
		{
			m_TTLLookup.Update(this);
			var ecb = m_ECBSystem.CreateCommandBuffer();
			LuaTTLBridge.UpdateContext(m_TTLLookup, ecb);

			var deltaTime = SystemAPI.Time.DeltaTime;

			foreach (var (ttl, entity) in SystemAPI.Query<RefRW<LuaTimeToLive>>().WithEntityAccess())
			{
				if (ttl.ValueRO.remaining <= 0f)
					continue;

				ttl.ValueRW.remaining -= deltaTime;

				if (ttl.ValueRO.remaining <= 0f)
				{
					ttl.ValueRW.remaining = 0f;

					// Fire OnExpire event if entity has event buffer
					if (SystemAPI.HasBuffer<LuaEvent>(entity))
					{
						var events = SystemAPI.GetBuffer<LuaEvent>(entity);
						events.Add(
							new LuaEvent
							{
								eventName = "OnExpire",
								source = entity,
								target = entity,
								intParam = 0,
								floatParam = ttl.ValueRO.initial,
							}
						);
					}

					// Destroy entity if configured
					if (ttl.ValueRO.destroyOnExpire)
					{
						var entityId = LuaEntityRegistry.GetIdFromEntity(entity);
						if (entityId > 0)
						{
							LuaEntityRegistry.Destroy(entityId, ecb);
						}
						else
						{
							ecb.DestroyEntity(entity);
						}
					}
				}
			}
		}
	}
}
