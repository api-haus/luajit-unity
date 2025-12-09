namespace LuaGame.Systems
{
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaECS.Systems;
	using LuaGame.Bridge;
	using LuaGame.Components;
	using LuaGame.Core;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;

	/// <summary>
	/// Processes damage zones, applying damage to entities within range.
	/// Supports sphere, box, and capsule shapes.
	/// Uses the spatial grid for efficient queries.
	/// Runs before health system to ensure damage is applied before death checks.
	/// </summary>
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateAfter(typeof(LuaSpatialGridSystem))]
	[UpdateBefore(typeof(LuaHealthSystem))]
	public partial class LuaDamageZoneSystem : SystemBase
	{
		ComponentLookup<LuaHealth> m_HealthLookup;
		ComponentLookup<LuaTeam> m_TeamLookup;
		ComponentLookup<LocalTransform> m_TransformLookup;
		ComponentLookup<LuaDamageZone> m_ZoneLookup;
		BufferLookup<LuaDamageZoneHit> m_HitBufferLookup;
		EndSimulationEntityCommandBufferSystem m_ECBSystem;

		protected override void OnCreate()
		{
			m_HealthLookup = GetComponentLookup<LuaHealth>();
			m_TeamLookup = GetComponentLookup<LuaTeam>();
			m_TransformLookup = GetComponentLookup<LocalTransform>();
			m_ZoneLookup = GetComponentLookup<LuaDamageZone>();
			m_HitBufferLookup = GetBufferLookup<LuaDamageZoneHit>();
			m_ECBSystem = World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
		}

		/// <summary>
		/// Primes the damage zone context before OnInit runs.
		/// Called by GameModeManager to enable damagezone.create() during OnInit.
		/// </summary>
		public void PrimeContextForOnInit()
		{
			m_ZoneLookup.Update(this);
			m_HitBufferLookup.Update(this);
			var ecb = m_ECBSystem.CreateCommandBuffer();
			LuaDamageZoneBridge.UpdateContext(m_ZoneLookup, m_HitBufferLookup, ecb);
		}

		protected override void OnUpdate()
		{
			if (!LuaSpatialGrid.IsCreated)
				return;

			m_HealthLookup.Update(this);
			m_TeamLookup.Update(this);
			m_TransformLookup.Update(this);
			m_ZoneLookup.Update(this);
			m_HitBufferLookup.Update(this);

			// Update bridge context so Lua can interact with damage zones
			var ecb = m_ECBSystem.CreateCommandBuffer();
			LuaDamageZoneBridge.UpdateContext(m_ZoneLookup, m_HitBufferLookup, ecb);

			var deltaTime = SystemAPI.Time.DeltaTime;
			var elapsedTime = (float)SystemAPI.Time.ElapsedTime;

			// Process all damage zones
			foreach (
				var (zone, transform, hits, events, zoneEntity) in SystemAPI
					.Query<
						RefRW<LuaDamageZone>,
						RefRO<LocalTransform>,
						DynamicBuffer<LuaDamageZoneHit>,
						DynamicBuffer<LuaEvent>
					>()
					.WithEntityAccess()
			)
			{
				if (!zone.ValueRO.isActive)
					continue;

				// Update tick timer
				zone.ValueRW.tickTimer -= deltaTime;

				if (!zone.ValueRO.IsReadyToTick)
					continue;

				zone.ValueRW.ResetTickTimer();

				var zonePos = transform.ValueRO.Position;
				var zoneRot = transform.ValueRO.Rotation;

				// Query spatial grid based on shape
				var queryResults = new NativeList<SpatialEntry>(32, Allocator.Temp);

				switch (zone.ValueRO.shape)
				{
					case DamageZoneShape.Sphere:
						LuaSpatialGrid.QuerySphere(zonePos, zone.ValueRO.radius, ref queryResults);
						break;

					case DamageZoneShape.Box:
						LuaSpatialGrid.QueryBox(zonePos, zone.ValueRO.extents, ref queryResults);
						break;

					case DamageZoneShape.Capsule:
						var capsuleRadius = zone.ValueRO.extents.x;
						var halfHeight = zone.ValueRO.extents.y;
						var up = math.mul(zoneRot, math.up());
						var pointA = zonePos + up * halfHeight;
						var pointB = zonePos - up * halfHeight;
						LuaSpatialGrid.QueryCapsule(pointA, pointB, capsuleRadius, ref queryResults);
						break;
				}

				var hitCount = 0;
				var totalDamage = 0f;

				// Process hits
				for (var i = 0; i < queryResults.Length; i++)
				{
					var entry = queryResults[i];

					// Skip source entity
					if (entry.entityId == zone.ValueRO.sourceEntityId)
						continue;

					// Skip entities on cooldown
					if (IsOnCooldown(hits, entry.entityId, elapsedTime, zone.ValueRO.hitCooldown))
						continue;

					// Check team filtering
					if (!CanDamageTeam(zone.ValueRO, entry.entity))
						continue;

					// Apply damage
					if (m_HealthLookup.HasComponent(entry.entity))
					{
						var health = m_HealthLookup[entry.entity];
						health.TakeDamage(zone.ValueRO.damage);
						m_HealthLookup[entry.entity] = health;

						hitCount++;
						totalDamage += zone.ValueRO.damage;

						// Record hit
						RecordHit(hits, entry.entityId, elapsedTime, zone.ValueRO.damage);

						// Fire damage events
						FireDamageEvents(events, zoneEntity, entry.entity, zone.ValueRO.damage, health.current);
					}
				}

				// Fire zone tick event if any hits
				if (hitCount > 0)
				{
					events.Add(
						new LuaEvent
						{
							eventName = "OnZoneTick",
							source = zoneEntity,
							target = zoneEntity,
							intParam = hitCount,
							floatParam = totalDamage,
						}
					);
				}

				queryResults.Dispose();
			}
		}

		bool CanDamageTeam(LuaDamageZone zone, Entity targetEntity)
		{
			// If zone has no team mask, damage all
			if (zone.teamMask == -1)
				return true;

			// If target has no team, can be damaged
			if (!m_TeamLookup.HasComponent(targetEntity))
				return true;

			var targetTeam = m_TeamLookup[targetEntity];
			return (zone.teamMask & (1 << targetTeam.teamId)) != 0;
		}

		static bool IsOnCooldown(
			DynamicBuffer<LuaDamageZoneHit> hits,
			int entityId,
			float currentTime,
			float cooldown
		)
		{
			for (var i = 0; i < hits.Length; i++)
			{
				if (hits[i].entityId == entityId)
				{
					return currentTime - hits[i].lastHitTime < cooldown;
				}
			}

			return false;
		}

		static void RecordHit(
			DynamicBuffer<LuaDamageZoneHit> hits,
			int entityId,
			float currentTime,
			float damage
		)
		{
			// Update existing entry or add new
			for (var i = 0; i < hits.Length; i++)
			{
				if (hits[i].entityId == entityId)
				{
					var hit = hits[i];
					hit.lastHitTime = currentTime;
					hit.totalDamage += damage;
					hits[i] = hit;
					return;
				}
			}

			// Add new entry
			hits.Add(
				new LuaDamageZoneHit
				{
					entityId = entityId,
					lastHitTime = currentTime,
					totalDamage = damage,
				}
			);
		}

		void FireDamageEvents(
			DynamicBuffer<LuaEvent> events,
			Entity source,
			Entity target,
			float damage,
			float remainingHealth
		)
		{
			// OnDamageDealt for the zone
			events.Add(
				new LuaEvent
				{
					eventName = "OnDamageDealt",
					source = source,
					target = target,
					intParam = (int)damage,
					floatParam = damage,
				}
			);

			// OnDamageTaken for the target (if it has event buffer)
			if (SystemAPI.HasBuffer<LuaEvent>(target))
			{
				var targetEvents = SystemAPI.GetBuffer<LuaEvent>(target);
				targetEvents.Add(
					new LuaEvent
					{
						eventName = "OnDamageTaken",
						source = target,
						target = source,
						intParam = (int)damage,
						floatParam = remainingHealth,
					}
				);
			}
		}

		protected override void OnDestroy()
		{
			LuaDamageZoneBridge.ClearContext();
		}
	}
}
