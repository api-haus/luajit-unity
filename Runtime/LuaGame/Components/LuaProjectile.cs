namespace LuaGame.Components
{
	using Unity.Entities;
	using Unity.Mathematics;

	/// <summary>
	/// Projectile component for moving damage sources.
	/// Supports piercing (hit multiple targets) and bouncing (reflect off surfaces).
	/// Uses continuous sweep detection for fast-moving projectiles.
	/// </summary>
	public struct LuaProjectile : IComponentData
	{
		/// <summary>
		/// Movement velocity (direction * speed).
		/// </summary>
		public float3 velocity;

		/// <summary>
		/// Base speed (magnitude of velocity).
		/// </summary>
		public float speed;

		/// <summary>
		/// Collision radius for hit detection.
		/// </summary>
		public float radius;

		/// <summary>
		/// Damage dealt on hit.
		/// </summary>
		public float damage;

		/// <summary>
		/// Remaining pierces (-1 = infinite, 0 = destroy on hit).
		/// Each hit decrements this value.
		/// </summary>
		public int pierceCount;

		/// <summary>
		/// Original pierce count (for resetting).
		/// </summary>
		public int maxPierces;

		/// <summary>
		/// Remaining bounces (-1 = infinite, 0 = no bouncing).
		/// Each reflection decrements this value.
		/// </summary>
		public int bounceCount;

		/// <summary>
		/// Original bounce count (for resetting).
		/// </summary>
		public int maxBounces;

		/// <summary>
		/// Source entity ID (avoid self-damage).
		/// </summary>
		public int sourceEntityId;

		/// <summary>
		/// Team ID for filtering (0 = damage all).
		/// </summary>
		public int teamId;

		/// <summary>
		/// Cooldown before hitting same target again (seconds).
		/// </summary>
		public float hitCooldown;

		/// <summary>
		/// Previous frame position (for sweep detection).
		/// </summary>
		public float3 previousPosition;

		/// <summary>
		/// If true, projectile is active and should be processed.
		/// </summary>
		public bool isActive;

		/// <summary>
		/// Total entities hit by this projectile.
		/// </summary>
		public int totalHits;

		/// <summary>
		/// Creates a simple projectile.
		/// </summary>
		public static LuaProjectile Create(
			float3 direction,
			float speed,
			float damage,
			float radius = 0.1f,
			int sourceEntityId = -1
		)
		{
			return new LuaProjectile
			{
				velocity = math.normalizesafe(direction) * speed,
				speed = speed,
				radius = radius,
				damage = damage,
				pierceCount = 0,
				maxPierces = 0,
				bounceCount = 0,
				maxBounces = 0,
				sourceEntityId = sourceEntityId,
				teamId = 0,
				hitCooldown = 0.5f,
				previousPosition = float3.zero,
				isActive = true,
				totalHits = 0,
			};
		}

		/// <summary>
		/// Creates a piercing projectile.
		/// </summary>
		public static LuaProjectile CreatePiercing(
			float3 direction,
			float speed,
			float damage,
			int pierceCount,
			float radius = 0.1f,
			int sourceEntityId = -1
		)
		{
			var proj = Create(direction, speed, damage, radius, sourceEntityId);
			proj.pierceCount = pierceCount;
			proj.maxPierces = pierceCount;
			return proj;
		}

		/// <summary>
		/// Creates a bouncing projectile.
		/// </summary>
		public static LuaProjectile CreateBouncing(
			float3 direction,
			float speed,
			float damage,
			int bounceCount,
			float radius = 0.1f,
			int sourceEntityId = -1
		)
		{
			var proj = Create(direction, speed, damage, radius, sourceEntityId);
			proj.bounceCount = bounceCount;
			proj.maxBounces = bounceCount;
			return proj;
		}

		/// <summary>
		/// Returns true if projectile should be destroyed after hit.
		/// </summary>
		public bool ShouldDestroyOnHit => pierceCount == 0;

		/// <summary>
		/// Returns true if projectile can pierce through target.
		/// </summary>
		public bool CanPierce => pierceCount != 0;

		/// <summary>
		/// Returns true if projectile can bounce.
		/// </summary>
		public bool CanBounce => bounceCount != 0;

		/// <summary>
		/// Decrements pierce count after hitting a target.
		/// </summary>
		public void OnHit()
		{
			totalHits++;
			if (pierceCount > 0)
				pierceCount--;
		}

		/// <summary>
		/// Decrements bounce count and reflects velocity.
		/// </summary>
		public void OnBounce(float3 surfaceNormal)
		{
			if (bounceCount > 0)
				bounceCount--;

			velocity = math.reflect(velocity, surfaceNormal);
		}

		/// <summary>
		/// Sets the direction while maintaining speed.
		/// </summary>
		public void SetDirection(float3 direction)
		{
			velocity = math.normalizesafe(direction) * speed;
		}

		/// <summary>
		/// Sets the speed while maintaining direction.
		/// </summary>
		public void SetSpeed(float newSpeed)
		{
			var dir = math.normalizesafe(velocity);
			speed = newSpeed;
			velocity = dir * speed;
		}
	}

	/// <summary>
	/// Buffer element tracking entities hit by a projectile.
	/// Used for cooldown tracking to prevent hitting same entity too frequently.
	/// </summary>
	public struct LuaProjectileHit : IBufferElementData
	{
		/// <summary>
		/// Entity ID that was hit.
		/// </summary>
		public int entityId;

		/// <summary>
		/// Time of hit (for cooldown).
		/// </summary>
		public float hitTime;

		/// <summary>
		/// Position where hit occurred.
		/// </summary>
		public float3 hitPosition;
	}
}
