namespace LuaGame.Tests
{
	using LuaGame.Components;
	using NUnit.Framework;

	/// <summary>
	/// Unit tests for LuaHealth component logic.
	/// </summary>
	public class LuaHealthComponentTest
	{
		[Test]
		public void Create_SetsMaxAndCurrentToSameValue()
		{
			var health = LuaHealth.Create(100f);

			Assert.AreEqual(100f, health.max);
			Assert.AreEqual(100f, health.current);
			Assert.IsFalse(health.isDead);
		}

		[Test]
		public void TakeDamage_ReducesCurrentHealth()
		{
			var health = LuaHealth.Create(100f);

			health.TakeDamage(30f);

			Assert.AreEqual(70f, health.current);
			Assert.IsFalse(health.isDead);
		}

		[Test]
		public void TakeDamage_SetsDeadWhenHealthReachesZero()
		{
			var health = LuaHealth.Create(100f);

			health.TakeDamage(100f);

			Assert.AreEqual(0f, health.current);
			Assert.IsTrue(health.isDead);
		}

		[Test]
		public void TakeDamage_ClampsToZero()
		{
			var health = LuaHealth.Create(100f);

			health.TakeDamage(150f);

			Assert.AreEqual(0f, health.current);
			Assert.IsTrue(health.isDead);
		}

		[Test]
		public void TakeDamage_IgnoredWhenDead()
		{
			var health = LuaHealth.Create(100f);
			health.TakeDamage(100f);
			Assert.IsTrue(health.isDead);

			health.TakeDamage(50f);

			Assert.AreEqual(0f, health.current);
		}

		[Test]
		public void Heal_IncreasesCurrentHealth()
		{
			var health = LuaHealth.Create(100f);
			health.TakeDamage(50f);

			health.Heal(30f);

			Assert.AreEqual(80f, health.current);
		}

		[Test]
		public void Heal_ClampsToMax()
		{
			var health = LuaHealth.Create(100f);
			health.TakeDamage(20f);

			health.Heal(50f);

			Assert.AreEqual(100f, health.current);
		}

		[Test]
		public void Heal_IgnoredWhenDead()
		{
			var health = LuaHealth.Create(100f);
			health.TakeDamage(100f);
			Assert.IsTrue(health.isDead);

			health.Heal(50f);

			Assert.AreEqual(0f, health.current);
			Assert.IsTrue(health.isDead);
		}

		[Test]
		public void HealthPercent_ReturnsCorrectRatio()
		{
			var health = LuaHealth.Create(100f);
			Assert.AreEqual(1f, health.HealthPercent);

			health.TakeDamage(25f);
			Assert.AreEqual(0.75f, health.HealthPercent);

			health.TakeDamage(50f);
			Assert.AreEqual(0.25f, health.HealthPercent);
		}

		[Test]
		public void HealthPercent_ReturnsZeroWhenMaxIsZero()
		{
			var health = new LuaHealth
			{
				current = 0,
				max = 0,
				isDead = false,
			};

			Assert.AreEqual(0f, health.HealthPercent);
		}
	}
}
