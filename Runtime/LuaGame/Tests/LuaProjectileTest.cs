namespace LuaGame.Tests
{
	using LuaGame.Components;
	using NUnit.Framework;
	using Unity.Mathematics;

	[TestFixture]
	public class LuaProjectileTest
	{
		[Test]
		public void Create_SetsCorrectValues()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 25f);

			Assert.AreEqual(10f, proj.speed);
			Assert.AreEqual(25f, proj.damage);
			Assert.AreEqual(new float3(10, 0, 0), proj.velocity);
			Assert.IsTrue(proj.isActive);
			Assert.AreEqual(0, proj.pierceCount);
			Assert.AreEqual(0, proj.bounceCount);
		}

		[Test]
		public void Create_NormalizesDirection()
		{
			var proj = LuaProjectile.Create(
				direction: new float3(2, 0, 0), // Not normalized
				speed: 10f,
				damage: 25f
			);

			Assert.AreEqual(10f, math.length(proj.velocity), 0.001f);
		}

		[Test]
		public void Create_ZeroDirection_HandlesGracefully()
		{
			var proj = LuaProjectile.Create(direction: float3.zero, speed: 10f, damage: 25f);

			// Should handle zero direction without NaN
			Assert.IsFalse(math.any(math.isnan(proj.velocity)));
		}

		[Test]
		public void CreatePiercing_SetsPierceCount()
		{
			var proj = LuaProjectile.CreatePiercing(
				direction: new float3(0, 0, 1),
				speed: 20f,
				damage: 15f,
				pierceCount: 3
			);

			Assert.AreEqual(3, proj.pierceCount);
			Assert.AreEqual(3, proj.maxPierces);
			Assert.IsTrue(proj.CanPierce);
			Assert.IsFalse(proj.ShouldDestroyOnHit);
		}

		[Test]
		public void CreatePiercing_InfinitePierce()
		{
			var proj = LuaProjectile.CreatePiercing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				pierceCount: -1
			);

			Assert.AreEqual(-1, proj.pierceCount);
			Assert.AreEqual(-1, proj.maxPierces);
			Assert.IsTrue(proj.CanPierce);
			Assert.IsFalse(proj.ShouldDestroyOnHit);
		}

		[Test]
		public void CreateBouncing_SetsBounceCount()
		{
			var proj = LuaProjectile.CreateBouncing(
				direction: new float3(0, 1, 0),
				speed: 15f,
				damage: 10f,
				bounceCount: 5
			);

			Assert.AreEqual(5, proj.bounceCount);
			Assert.AreEqual(5, proj.maxBounces);
			Assert.IsTrue(proj.CanBounce);
		}

		[Test]
		public void CreateBouncing_InfiniteBounce()
		{
			var proj = LuaProjectile.CreateBouncing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				bounceCount: -1
			);

			Assert.AreEqual(-1, proj.bounceCount);
			Assert.IsTrue(proj.CanBounce);
		}

		[Test]
		public void OnHit_DecrementsPierceCount()
		{
			var proj = LuaProjectile.CreatePiercing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				pierceCount: 2
			);

			proj.OnHit();

			Assert.AreEqual(1, proj.pierceCount);
			Assert.AreEqual(1, proj.totalHits);
		}

		[Test]
		public void OnHit_InfinitePierce_DoesNotDecrement()
		{
			var proj = LuaProjectile.CreatePiercing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				pierceCount: -1
			);

			proj.OnHit();
			proj.OnHit();
			proj.OnHit();

			Assert.AreEqual(-1, proj.pierceCount);
			Assert.AreEqual(3, proj.totalHits);
		}

		[Test]
		public void OnHit_TracksMultipleHits()
		{
			var proj = LuaProjectile.CreatePiercing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				pierceCount: 5
			);

			proj.OnHit();
			proj.OnHit();
			proj.OnHit();

			Assert.AreEqual(3, proj.totalHits);
			Assert.AreEqual(2, proj.pierceCount);
		}

		[Test]
		public void ShouldDestroyOnHit_WhenPierceZero()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			Assert.IsTrue(proj.ShouldDestroyOnHit);
		}

		[Test]
		public void ShouldDestroyOnHit_WhenPierceRemaining()
		{
			var proj = LuaProjectile.CreatePiercing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				pierceCount: 1
			);

			Assert.IsFalse(proj.ShouldDestroyOnHit);
		}

		[Test]
		public void ShouldDestroyOnHit_AfterAllPiercesUsed()
		{
			var proj = LuaProjectile.CreatePiercing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				pierceCount: 1
			);

			proj.OnHit();

			Assert.IsTrue(proj.ShouldDestroyOnHit);
		}

		[Test]
		public void OnBounce_ReflectsVelocity()
		{
			var proj = LuaProjectile.CreateBouncing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				bounceCount: 3
			);

			proj.OnBounce(new float3(-1, 0, 0)); // Surface normal pointing back

			Assert.AreEqual(2, proj.bounceCount);
			// Velocity should be reflected
			Assert.Less(proj.velocity.x, 0f, "Velocity should be reflected");
		}

		[Test]
		public void OnBounce_MaintainsSpeed()
		{
			var proj = LuaProjectile.CreateBouncing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				bounceCount: 3
			);

			proj.OnBounce(new float3(0, 1, 0)); // Surface normal pointing up

			Assert.AreEqual(10f, math.length(proj.velocity), 0.01f);
		}

		[Test]
		public void OnBounce_InfiniteBounce_DoesNotDecrement()
		{
			var proj = LuaProjectile.CreateBouncing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				bounceCount: -1
			);

			proj.OnBounce(new float3(-1, 0, 0));
			proj.OnBounce(new float3(1, 0, 0));

			Assert.AreEqual(-1, proj.bounceCount);
		}

		[Test]
		public void OnBounce_DiagonalReflection()
		{
			var proj = LuaProjectile.CreateBouncing(
				direction: math.normalize(new float3(1, -1, 0)),
				speed: 10f,
				damage: 10f,
				bounceCount: 3
			);

			proj.OnBounce(new float3(0, 1, 0)); // Ground normal

			Assert.Greater(proj.velocity.y, 0f, "Should bounce upward");
		}

		[Test]
		public void SetDirection_MaintainsSpeed()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			proj.SetDirection(new float3(0, 1, 0));

			Assert.AreEqual(10f, math.length(proj.velocity), 0.001f);
			Assert.AreEqual(10f, proj.velocity.y, 0.001f);
		}

		[Test]
		public void SetDirection_NormalizesInput()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			proj.SetDirection(new float3(5, 0, 0)); // Not normalized

			Assert.AreEqual(10f, math.length(proj.velocity), 0.001f);
		}

		[Test]
		public void SetSpeed_MaintainsDirection()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			proj.SetSpeed(20f);

			Assert.AreEqual(20f, proj.speed);
			Assert.AreEqual(20f, proj.velocity.x, 0.001f);
		}

		[Test]
		public void SetSpeed_ZeroSpeed()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			proj.SetSpeed(0f);

			Assert.AreEqual(0f, proj.speed);
			Assert.AreEqual(float3.zero, proj.velocity);
		}

		[Test]
		public void CanPierce_WhenInfinite()
		{
			var proj = LuaProjectile.CreatePiercing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				pierceCount: -1
			);

			Assert.IsTrue(proj.CanPierce);
		}

		[Test]
		public void CanPierce_WhenPositive()
		{
			var proj = LuaProjectile.CreatePiercing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				pierceCount: 5
			);

			Assert.IsTrue(proj.CanPierce);
		}

		[Test]
		public void CanPierce_WhenZero()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			Assert.IsFalse(proj.CanPierce);
		}

		[Test]
		public void CanBounce_WhenInfinite()
		{
			var proj = LuaProjectile.CreateBouncing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				bounceCount: -1
			);

			Assert.IsTrue(proj.CanBounce);
		}

		[Test]
		public void CanBounce_WhenPositive()
		{
			var proj = LuaProjectile.CreateBouncing(
				direction: new float3(1, 0, 0),
				speed: 10f,
				damage: 10f,
				bounceCount: 3
			);

			Assert.IsTrue(proj.CanBounce);
		}

		[Test]
		public void CanBounce_WhenZero()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			Assert.IsFalse(proj.CanBounce);
		}

		[Test]
		public void IsActive_DefaultTrue()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			Assert.IsTrue(proj.isActive);
		}

		[Test]
		public void IsActive_CanBeDisabled()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			proj.isActive = false;

			Assert.IsFalse(proj.isActive);
		}

		[Test]
		public void SourceEntityId_DefaultMinusOne()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			Assert.AreEqual(-1, proj.sourceEntityId, "Default source is -1 (no source)");
		}

		[Test]
		public void SourceEntityId_CanBeSet()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			proj.sourceEntityId = 42;

			Assert.AreEqual(42, proj.sourceEntityId);
		}

		[Test]
		public void TeamId_DefaultZero()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			Assert.AreEqual(0, proj.teamId);
		}

		[Test]
		public void HitCooldown_DefaultHalfSecond()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			Assert.AreEqual(0.5f, proj.hitCooldown, "Default hit cooldown is 0.5 seconds");
		}

		[Test]
		public void Radius_DefaultPointOne()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			Assert.AreEqual(0.1f, proj.radius, "Default radius is 0.1f");
		}

		[Test]
		public void PreviousPosition_TracksMovement()
		{
			var proj = LuaProjectile.Create(direction: new float3(1, 0, 0), speed: 10f, damage: 10f);

			var startPos = new float3(5, 10, 15);
			proj.previousPosition = startPos;

			Assert.AreEqual(startPos, proj.previousPosition);
		}
	}

	[TestFixture]
	public class LuaProjectileHitTest
	{
		[Test]
		public void ProjectileHit_StoresData()
		{
			var hit = new LuaProjectileHit
			{
				entityId = 123,
				hitTime = 2.5f,
				hitPosition = new float3(1, 2, 3),
			};

			Assert.AreEqual(123, hit.entityId);
			Assert.AreEqual(2.5f, hit.hitTime);
			Assert.AreEqual(new float3(1, 2, 3), hit.hitPosition);
		}

		[Test]
		public void ProjectileHit_DefaultValues()
		{
			var hit = new LuaProjectileHit();

			Assert.AreEqual(0, hit.entityId);
			Assert.AreEqual(0f, hit.hitTime);
			Assert.AreEqual(float3.zero, hit.hitPosition);
		}
	}
}
