namespace LuaGame.Components
{
	using Unity.Entities;
	using Unity.Mathematics;

	/// <summary>
	/// Shape types for damage zones.
	/// </summary>
	public enum DamageZoneShape : byte
	{
		Sphere,
		Box,
		Capsule,
	}

	/// <summary>
	/// Damage zone component for area-of-effect damage.
	/// Applies damage to entities within the zone at regular intervals.
	/// </summary>
	public struct LuaDamageZone : IComponentData
	{
		/// <summary>
		/// Damage per tick.
		/// </summary>
		public float damage;

		/// <summary>
		/// Zone radius (for sphere shape).
		/// </summary>
		public float radius;

		/// <summary>
		/// Extents for box/capsule shapes (half-size).
		/// For capsule: x = radius, y = half-height.
		/// </summary>
		public float3 extents;

		/// <summary>
		/// Shape of the damage zone.
		/// </summary>
		public DamageZoneShape shape;

		/// <summary>
		/// Time between damage ticks (0 = every frame).
		/// </summary>
		public float tickInterval;

		/// <summary>
		/// Countdown to next tick.
		/// </summary>
		public float tickTimer;

		/// <summary>
		/// Bitmask for team filtering.
		/// Only entities with matching enemy mask receive damage.
		/// </summary>
		public int teamMask;

		/// <summary>
		/// Source entity ID (for avoiding self-damage).
		/// </summary>
		public int sourceEntityId;

		/// <summary>
		/// If true, zone is currently active.
		/// </summary>
		public bool isActive;

		/// <summary>
		/// Cooldown between hitting same entity again.
		/// </summary>
		public float hitCooldown;

		/// <summary>
		/// Creates a sphere damage zone.
		/// </summary>
		public static LuaDamageZone CreateSphere(
			float radius,
			float damage,
			float tickInterval = 0f,
			int sourceEntityId = -1
		)
		{
			return new LuaDamageZone
			{
				shape = DamageZoneShape.Sphere,
				radius = radius,
				extents = float3.zero,
				damage = damage,
				tickInterval = tickInterval,
				tickTimer = 0f,
				teamMask = -1, // All teams
				sourceEntityId = sourceEntityId,
				isActive = true,
				hitCooldown = tickInterval,
			};
		}

		/// <summary>
		/// Creates a box damage zone.
		/// </summary>
		public static LuaDamageZone CreateBox(
			float3 extents,
			float damage,
			float tickInterval = 0f,
			int sourceEntityId = -1
		)
		{
			return new LuaDamageZone
			{
				shape = DamageZoneShape.Box,
				radius = math.length(extents), // Bounding radius
				extents = extents,
				damage = damage,
				tickInterval = tickInterval,
				tickTimer = 0f,
				teamMask = -1,
				sourceEntityId = sourceEntityId,
				isActive = true,
				hitCooldown = tickInterval,
			};
		}

		/// <summary>
		/// Creates a capsule damage zone.
		/// </summary>
		public static LuaDamageZone CreateCapsule(
			float capsuleRadius,
			float halfHeight,
			float damage,
			float tickInterval = 0f,
			int sourceEntityId = -1
		)
		{
			return new LuaDamageZone
			{
				shape = DamageZoneShape.Capsule,
				radius = capsuleRadius + halfHeight, // Bounding radius
				extents = new float3(capsuleRadius, halfHeight, 0),
				damage = damage,
				tickInterval = tickInterval,
				tickTimer = 0f,
				teamMask = -1,
				sourceEntityId = sourceEntityId,
				isActive = true,
				hitCooldown = tickInterval,
			};
		}

		/// <summary>
		/// Returns true if ready to tick (tickTimer <= 0).
		/// </summary>
		public bool IsReadyToTick => tickTimer <= 0f;

		/// <summary>
		/// Resets the tick timer.
		/// </summary>
		public void ResetTickTimer()
		{
			tickTimer = tickInterval;
		}
	}

	/// <summary>
	/// Buffer element tracking entities hit by a damage zone.
	/// Used for cooldown tracking to prevent hitting same entity too frequently.
	/// </summary>
	public struct LuaDamageZoneHit : IBufferElementData
	{
		/// <summary>
		/// Entity ID that was hit.
		/// </summary>
		public int entityId;

		/// <summary>
		/// Time of last hit (for cooldown).
		/// </summary>
		public float lastHitTime;

		/// <summary>
		/// Total damage dealt to this entity.
		/// </summary>
		public float totalDamage;
	}
}
