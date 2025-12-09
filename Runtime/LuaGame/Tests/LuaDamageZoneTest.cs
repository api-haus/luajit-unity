namespace LuaGame.Tests
{
	using LuaGame.Components;
	using NUnit.Framework;
	using Unity.Mathematics;

	[TestFixture]
	public class LuaDamageZoneTest
	{
		[Test]
		public void CreateSphere_SetsCorrectValues()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);

			Assert.AreEqual(DamageZoneShape.Sphere, zone.shape);
			Assert.AreEqual(5f, zone.radius);
			Assert.AreEqual(10f, zone.damage);
			Assert.IsTrue(zone.isActive);
			Assert.AreEqual(-1, zone.teamMask);
		}

		[Test]
		public void CreateSphere_WithTickInterval()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f, tickInterval: 0.5f);

			Assert.AreEqual(0.5f, zone.tickInterval);
			Assert.AreEqual(0.5f, zone.hitCooldown);
		}

		[Test]
		public void CreateSphere_DefaultTickIntervalIsZero()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);

			Assert.AreEqual(0f, zone.tickInterval);
		}

		[Test]
		public void CreateBox_SetsCorrectValues()
		{
			var extents = new float3(2, 3, 4);
			var zone = LuaDamageZone.CreateBox(extents, 15f);

			Assert.AreEqual(DamageZoneShape.Box, zone.shape);
			Assert.AreEqual(extents, zone.extents);
			Assert.AreEqual(15f, zone.damage);
			Assert.Greater(zone.radius, 0f, "Bounding radius should be set");
		}

		[Test]
		public void CreateBox_BoundingRadiusIsCorrect()
		{
			var extents = new float3(3, 4, 0); // 3-4-5 triangle
			var zone = LuaDamageZone.CreateBox(extents, 10f);

			// Bounding radius should be length of extents vector
			var expectedRadius = math.length(extents);
			Assert.AreEqual(expectedRadius, zone.radius, 0.001f);
		}

		[Test]
		public void CreateCapsule_SetsCorrectValues()
		{
			var zone = LuaDamageZone.CreateCapsule(1f, 2f, 20f);

			Assert.AreEqual(DamageZoneShape.Capsule, zone.shape);
			Assert.AreEqual(1f, zone.extents.x, "Capsule radius");
			Assert.AreEqual(2f, zone.extents.y, "Capsule half-height");
			Assert.AreEqual(20f, zone.damage);
		}

		[Test]
		public void CreateCapsule_BoundingRadiusIncludesHeight()
		{
			var capsuleRadius = 1f;
			var halfHeight = 2f;
			var zone = LuaDamageZone.CreateCapsule(capsuleRadius, halfHeight, 10f);

			// Bounding radius should encompass entire capsule
			Assert.GreaterOrEqual(zone.radius, halfHeight + capsuleRadius);
		}

		[Test]
		public void IsReadyToTick_WhenTimerZero()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);
			zone.tickTimer = 0f;

			Assert.IsTrue(zone.IsReadyToTick);
		}

		[Test]
		public void IsReadyToTick_WhenTimerNegative()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);
			zone.tickTimer = -0.5f;

			Assert.IsTrue(zone.IsReadyToTick);
		}

		[Test]
		public void IsReadyToTick_WhenTimerPositive()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);
			zone.tickTimer = 1f;

			Assert.IsFalse(zone.IsReadyToTick);
		}

		[Test]
		public void ResetTickTimer_SetsToInterval()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f, tickInterval: 0.5f);
			zone.tickTimer = 0f;

			zone.ResetTickTimer();

			Assert.AreEqual(0.5f, zone.tickTimer);
		}

		[Test]
		public void ResetTickTimer_ZeroIntervalStaysZero()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f, tickInterval: 0f);

			zone.ResetTickTimer();

			Assert.AreEqual(0f, zone.tickTimer);
		}

		[Test]
		public void TickInterval_ControlsDamageRate()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f, tickInterval: 1f);

			Assert.AreEqual(1f, zone.tickInterval);
			Assert.AreEqual(1f, zone.hitCooldown);
		}

		[Test]
		public void IsActive_DefaultTrue()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);

			Assert.IsTrue(zone.isActive);
		}

		[Test]
		public void IsActive_CanBeDisabled()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);
			zone.isActive = false;

			Assert.IsFalse(zone.isActive);
		}

		[Test]
		public void TeamMask_DefaultIsMinusOne()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);

			Assert.AreEqual(-1, zone.teamMask, "Default mask should damage all teams");
		}

		[Test]
		public void TeamMask_CanBeSet()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);
			zone.teamMask = 0b1010; // Teams 1 and 3

			Assert.AreEqual(0b1010, zone.teamMask);
		}

		[Test]
		public void SourceEntityId_DefaultIsMinusOne()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);

			Assert.AreEqual(-1, zone.sourceEntityId, "Default source is -1 (no source)");
		}

		[Test]
		public void SourceEntityId_CanBeSet()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);
			zone.sourceEntityId = 42;

			Assert.AreEqual(42, zone.sourceEntityId);
		}

		[Test]
		public void Damage_CanBeModified()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);
			zone.damage = 25f;

			Assert.AreEqual(25f, zone.damage);
		}

		[Test]
		public void Radius_CanBeModified()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 10f);
			zone.radius = 10f;

			Assert.AreEqual(10f, zone.radius);
		}

		[Test]
		public void ZeroDamage_IsValid()
		{
			var zone = LuaDamageZone.CreateSphere(5f, 0f);

			Assert.AreEqual(0f, zone.damage);
		}

		[Test]
		public void NegativeDamage_IsValidForHealing()
		{
			var zone = LuaDamageZone.CreateSphere(5f, -10f);

			Assert.AreEqual(-10f, zone.damage);
		}
	}

	[TestFixture]
	public class LuaDamageZoneHitTest
	{
		[Test]
		public void DamageZoneHit_StoresData()
		{
			var hit = new LuaDamageZoneHit
			{
				entityId = 42,
				lastHitTime = 1.5f,
				totalDamage = 100f,
			};

			Assert.AreEqual(42, hit.entityId);
			Assert.AreEqual(1.5f, hit.lastHitTime);
			Assert.AreEqual(100f, hit.totalDamage);
		}

		[Test]
		public void DamageZoneHit_DefaultValues()
		{
			var hit = new LuaDamageZoneHit();

			Assert.AreEqual(0, hit.entityId);
			Assert.AreEqual(0f, hit.lastHitTime);
			Assert.AreEqual(0f, hit.totalDamage);
		}

		[Test]
		public void DamageZoneHit_CanAccumulateDamage()
		{
			var hit = new LuaDamageZoneHit { entityId = 1, totalDamage = 10f };
			hit.totalDamage += 15f;

			Assert.AreEqual(25f, hit.totalDamage);
		}
	}

	[TestFixture]
	public class DamageZoneShapeTest
	{
		[Test]
		public void DamageZoneShape_SphereValue()
		{
			Assert.AreEqual(0, (int)DamageZoneShape.Sphere);
		}

		[Test]
		public void DamageZoneShape_BoxValue()
		{
			Assert.AreEqual(1, (int)DamageZoneShape.Box);
		}

		[Test]
		public void DamageZoneShape_CapsuleValue()
		{
			Assert.AreEqual(2, (int)DamageZoneShape.Capsule);
		}
	}
}
