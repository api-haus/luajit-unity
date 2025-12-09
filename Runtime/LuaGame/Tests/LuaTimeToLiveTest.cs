namespace LuaGame.Tests
{
	using LuaGame.Components;
	using NUnit.Framework;

	/// <summary>
	/// Unit tests for LuaTimeToLive component.
	/// Note: System integration tests (decrement, expire events) require PlayMode
	/// because SystemAPI.Time.DeltaTime is 0 in EditMode test worlds.
	/// </summary>
	[TestFixture]
	public class LuaTimeToLiveTest
	{
		[Test]
		public void TimeToLive_Create_SetsValues()
		{
			var ttl = LuaTimeToLive.Create(10f);

			Assert.AreEqual(10f, ttl.remaining);
			Assert.AreEqual(10f, ttl.initial);
			Assert.IsTrue(ttl.destroyOnExpire);
			Assert.IsFalse(ttl.IsExpired);
		}

		[Test]
		public void TimeToLive_Progress_CalculatesCorrectly()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 5f;

			Assert.AreEqual(0.5f, ttl.Progress, 0.001f);
		}

		[Test]
		public void TimeToLive_Extend_AddsTime()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 5f;
			ttl.Extend(3f);

			Assert.AreEqual(8f, ttl.remaining);
		}

		[Test]
		public void TimeToLive_Extend_UpdatesInitialIfNeeded()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 10f;
			ttl.Extend(5f);

			Assert.AreEqual(15f, ttl.initial);
		}

		[Test]
		public void TimeToLive_Reset_RestoresToInitial()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 2f;
			ttl.Reset();

			Assert.AreEqual(10f, ttl.remaining);
		}

		[Test]
		public void TimeToLive_Create_ZeroDuration()
		{
			var ttl = LuaTimeToLive.Create(0f);

			Assert.AreEqual(0f, ttl.remaining);
			Assert.AreEqual(0f, ttl.initial);
			Assert.IsTrue(ttl.IsExpired);
		}

		[Test]
		public void TimeToLive_Create_NegativeDuration()
		{
			var ttl = LuaTimeToLive.Create(-5f);

			Assert.AreEqual(-5f, ttl.remaining);
			Assert.IsTrue(ttl.IsExpired);
		}

		[Test]
		public void TimeToLive_Create_WithDestroyFalse()
		{
			var ttl = LuaTimeToLive.Create(10f, destroyOnExpire: false);

			Assert.IsFalse(ttl.destroyOnExpire);
		}

		[Test]
		public void TimeToLive_Progress_ZeroInitial()
		{
			var ttl = new LuaTimeToLive
			{
				remaining = 5f,
				initial = 0f,
				destroyOnExpire = true,
			};

			Assert.AreEqual(1f, ttl.Progress, "Progress should be 1 when initial is 0");
		}

		[Test]
		public void TimeToLive_Progress_FullyRemaining()
		{
			var ttl = LuaTimeToLive.Create(10f);

			Assert.AreEqual(0f, ttl.Progress, 0.001f);
		}

		[Test]
		public void TimeToLive_Progress_Expired()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 0f;

			Assert.AreEqual(1f, ttl.Progress, 0.001f);
		}

		[Test]
		public void TimeToLive_Progress_NegativeRemaining()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = -5f;

			Assert.Greater(ttl.Progress, 1f, "Progress should exceed 1 when overdue");
		}

		[Test]
		public void TimeToLive_Extend_NegativeValue()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 8f;
			ttl.Extend(-3f);

			Assert.AreEqual(5f, ttl.remaining);
		}

		[Test]
		public void TimeToLive_Extend_ZeroValue()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 5f;
			ttl.Extend(0f);

			Assert.AreEqual(5f, ttl.remaining);
		}

		[Test]
		public void TimeToLive_Extend_DoesNotUpdateInitialWhenBelowCurrent()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 5f;
			ttl.Extend(2f);

			Assert.AreEqual(7f, ttl.remaining);
			Assert.AreEqual(10f, ttl.initial, "Initial should not change when remaining stays below it");
		}

		[Test]
		public void TimeToLive_Extend_FromExpired()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = -2f;
			ttl.Extend(5f);

			Assert.AreEqual(3f, ttl.remaining);
			Assert.IsFalse(ttl.IsExpired);
		}

		[Test]
		public void TimeToLive_Reset_WhenAlreadyAtInitial()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.Reset();

			Assert.AreEqual(10f, ttl.remaining);
		}

		[Test]
		public void TimeToLive_Reset_FromExpired()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = -5f;
			ttl.Reset();

			Assert.AreEqual(10f, ttl.remaining);
			Assert.IsFalse(ttl.IsExpired);
		}

		[Test]
		public void TimeToLive_IsExpired_ExactlyZero()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 0f;

			Assert.IsTrue(ttl.IsExpired);
		}

		[Test]
		public void TimeToLive_IsExpired_VerySmallPositive()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 0.0001f;

			Assert.IsFalse(ttl.IsExpired);
		}

		[Test]
		public void TimeToLive_VeryLargeDuration()
		{
			var ttl = LuaTimeToLive.Create(float.MaxValue / 2f);

			Assert.IsFalse(ttl.IsExpired);
			Assert.AreEqual(0f, ttl.Progress, 0.001f);
		}

		[Test]
		public void TimeToLive_DefaultStruct()
		{
			var ttl = new LuaTimeToLive();

			Assert.AreEqual(0f, ttl.remaining);
			Assert.AreEqual(0f, ttl.initial);
			Assert.IsFalse(ttl.destroyOnExpire);
			Assert.IsTrue(ttl.IsExpired);
		}

		[Test]
		public void TimeToLive_Progress_QuarterWay()
		{
			var ttl = LuaTimeToLive.Create(100f);
			ttl.remaining = 75f;

			Assert.AreEqual(0.25f, ttl.Progress, 0.001f);
		}

		[Test]
		public void TimeToLive_Progress_ThreeQuartersWay()
		{
			var ttl = LuaTimeToLive.Create(100f);
			ttl.remaining = 25f;

			Assert.AreEqual(0.75f, ttl.Progress, 0.001f);
		}

		[Test]
		public void TimeToLive_Extend_MultipleExtensions()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 5f;

			ttl.Extend(2f);
			ttl.Extend(3f);
			ttl.Extend(1f);

			Assert.AreEqual(11f, ttl.remaining);
			Assert.AreEqual(11f, ttl.initial, "Initial should track highest remaining");
		}

		[Test]
		public void TimeToLive_Reset_AfterExtend()
		{
			var ttl = LuaTimeToLive.Create(10f);
			ttl.remaining = 5f;
			ttl.Extend(10f); // Makes remaining 15, initial 15
			ttl.remaining = 3f;
			ttl.Reset();

			Assert.AreEqual(15f, ttl.remaining, "Should reset to new initial after extend");
		}
	}
}
