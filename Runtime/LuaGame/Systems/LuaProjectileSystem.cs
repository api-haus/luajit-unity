namespace LuaGame.Systems
{
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaECS.Systems;
	using LuaGame.Components;
	using LuaGame.Core;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;

	/// <summary>
	/// Processes projectiles: movement, continuous collision detection, damage.
	/// Uses sweep tests for accurate hit detection on fast projectiles.
	/// Runs before damage zone system.
	/// </summary>
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateAfter(typeof(LuaSpatialGridSystem))]
	[UpdateBefore(typeof(LuaDamageZoneSystem))]
	public partial class LuaProjectileSystem : SystemBase
	{
		EndSimulationEntityCommandBufferSystem m_ECBSystem;
		ComponentLookup<LuaHealth> m_HealthLookup;
		ComponentLookup<LuaTeam> m_TeamLookup;
		ComponentLookup<LocalTransform> m_TransformLookup;

		protected override void OnCreate()
		{
			m_ECBSystem = World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
			m_HealthLookup = GetComponentLookup<LuaHealth>();
			m_TeamLookup = GetComponentLookup<LuaTeam>();
			m_TransformLookup = GetComponentLookup<LocalTransform>();
		}

		protected override void OnUpdate()
		{
			if (!LuaSpatialGrid.IsCreated)
				return;

			m_HealthLookup.Update(this);
			m_TeamLookup.Update(this);
			m_TransformLookup.Update(this);

			var deltaTime = SystemAPI.Time.DeltaTime;
			var elapsedTime = (float)SystemAPI.Time.ElapsedTime;
			var ecb = m_ECBSystem.CreateCommandBuffer();

			// Process all projectiles
			foreach (
				var (projectile, transform, hits, events, projectileEntity) in SystemAPI
					.Query<
						RefRW<LuaProjectile>,
						RefRW<LocalTransform>,
						DynamicBuffer<LuaProjectileHit>,
						DynamicBuffer<LuaEvent>
					>()
					.WithEntityAccess()
			)
			{
				if (!projectile.ValueRO.isActive)
					continue;

				var currentPos = transform.ValueRO.Position;
				var prevPos = projectile.ValueRO.previousPosition;

				// On first frame, set previous position
				if (math.lengthsq(prevPos) < 0.0001f)
				{
					projectile.ValueRW.previousPosition = currentPos;
					prevPos = currentPos;
				}

				// Calculate new position
				var newPos = currentPos + projectile.ValueRO.velocity * deltaTime;

				// Continuous sweep detection
				var queryResults = new NativeList<SpatialEntry>(16, Allocator.Temp);
				LuaSpatialGrid.QueryLineSegment(
					prevPos,
					newPos,
					projectile.ValueRO.radius,
					ref queryResults
				);

				var shouldDestroy = false;
				var hitProcessed = false;

				// Process hits in order (sorted by distance from start)
				for (var i = 0; i < queryResults.Length; i++)
				{
					var entry = queryResults[i];

					// Skip source entity
					if (entry.entityId == projectile.ValueRO.sourceEntityId)
						continue;

					// Skip entities on cooldown
					if (IsOnCooldown(hits, entry.entityId, elapsedTime, projectile.ValueRO.hitCooldown))
						continue;

					// Check team filtering
					if (!CanDamageTarget(projectile.ValueRO, entry.entity))
						continue;

					// Calculate hit position (closest point on trajectory to target)
					var hitPos = CalculateHitPosition(prevPos, newPos, entry.position);

					// Apply damage
					if (m_HealthLookup.HasComponent(entry.entity))
					{
						var health = m_HealthLookup[entry.entity];
						health.TakeDamage(projectile.ValueRO.damage);
						m_HealthLookup[entry.entity] = health;

						projectile.ValueRW.OnHit();

						// Record hit
						RecordHit(hits, entry.entityId, elapsedTime, hitPos);

						// Fire damage events
						FireHitEvents(events, projectileEntity, entry.entity, projectile.ValueRO, hitPos);

						hitProcessed = true;

						// Check if should destroy
						if (projectile.ValueRO.ShouldDestroyOnHit)
						{
							shouldDestroy = true;
							// Set position to hit point
							newPos = hitPos;
							break;
						}
					}
				}

				queryResults.Dispose();

				// Update position
				transform.ValueRW.Position = newPos;
				projectile.ValueRW.previousPosition = currentPos;

				// Update rotation to face velocity direction
				if (math.lengthsq(projectile.ValueRO.velocity) > 0.0001f)
				{
					transform.ValueRW.Rotation = quaternion.LookRotationSafe(
						math.normalizesafe(projectile.ValueRO.velocity),
						math.up()
					);
				}

				// Destroy if needed
				if (shouldDestroy)
				{
					var entityId = LuaEntityRegistry.GetIdFromEntity(projectileEntity);
					if (entityId > 0)
					{
						LuaEntityRegistry.Destroy(entityId, ecb);
					}
					else
					{
						ecb.DestroyEntity(projectileEntity);
					}
				}
			}
		}

		bool CanDamageTarget(LuaProjectile projectile, Entity targetEntity)
		{
			// If projectile has no team filter, damage all
			if (projectile.teamId == 0)
				return true;

			// If target has no team, can be damaged
			if (!m_TeamLookup.HasComponent(targetEntity))
				return true;

			var targetTeam = m_TeamLookup[targetEntity];

			// Damage if target is not on same team
			return targetTeam.teamId != projectile.teamId;
		}

		static float3 CalculateHitPosition(float3 start, float3 end, float3 targetPos)
		{
			var direction = end - start;
			var length = math.length(direction);

			if (length < 0.0001f)
				return start;

			var normalizedDir = direction / length;
			var toTarget = targetPos - start;
			var projection = math.clamp(math.dot(toTarget, normalizedDir), 0f, length);

			return start + normalizedDir * projection;
		}

		static bool IsOnCooldown(
			DynamicBuffer<LuaProjectileHit> hits,
			int entityId,
			float currentTime,
			float cooldown
		)
		{
			for (var i = 0; i < hits.Length; i++)
			{
				if (hits[i].entityId == entityId)
				{
					return currentTime - hits[i].hitTime < cooldown;
				}
			}

			return false;
		}

		static void RecordHit(
			DynamicBuffer<LuaProjectileHit> hits,
			int entityId,
			float currentTime,
			float3 hitPosition
		)
		{
			// Update existing entry or add new
			for (var i = 0; i < hits.Length; i++)
			{
				if (hits[i].entityId == entityId)
				{
					var hit = hits[i];
					hit.hitTime = currentTime;
					hit.hitPosition = hitPosition;
					hits[i] = hit;
					return;
				}
			}

			// Add new entry
			hits.Add(
				new LuaProjectileHit
				{
					entityId = entityId,
					hitTime = currentTime,
					hitPosition = hitPosition,
				}
			);
		}

		void FireHitEvents(
			DynamicBuffer<LuaEvent> events,
			Entity projectileEntity,
			Entity targetEntity,
			LuaProjectile projectile,
			float3 hitPosition
		)
		{
			// OnProjectileHit for the projectile
			events.Add(
				new LuaEvent
				{
					eventName = "OnProjectileHit",
					source = projectileEntity,
					target = targetEntity,
					intParam = projectile.totalHits,
					floatParam = projectile.damage,
				}
			);

			// OnDamageTaken for the target
			if (SystemAPI.HasBuffer<LuaEvent>(targetEntity))
			{
				var targetEvents = SystemAPI.GetBuffer<LuaEvent>(targetEntity);
				targetEvents.Add(
					new LuaEvent
					{
						eventName = "OnDamageTaken",
						source = targetEntity,
						target = projectileEntity,
						intParam = (int)projectile.damage,
						floatParam = projectile.damage,
					}
				);
			}
		}
	}
}
