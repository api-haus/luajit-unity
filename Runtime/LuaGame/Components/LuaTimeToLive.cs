namespace LuaGame.Components
{
	using Unity.Entities;

	/// <summary>
	/// Time-to-live component for automatic entity destruction.
	/// Tracks remaining lifetime and optionally destroys entity when expired.
	/// </summary>
	public struct LuaTimeToLive : IComponentData
	{
		/// <summary>
		/// Time remaining before expiration (seconds).
		/// </summary>
		public float remaining;

		/// <summary>
		/// Original TTL value (for percentage calculations).
		/// </summary>
		public float initial;

		/// <summary>
		/// If true, entity is destroyed when remaining <= 0.
		/// If false, only fires OnExpire event.
		/// </summary>
		public bool destroyOnExpire;

		public static LuaTimeToLive Create(float duration, bool destroyOnExpire = true)
		{
			return new LuaTimeToLive
			{
				remaining = duration,
				initial = duration,
				destroyOnExpire = destroyOnExpire,
			};
		}

		/// <summary>
		/// Returns true if expired (remaining <= 0).
		/// </summary>
		public bool IsExpired => remaining <= 0f;

		/// <summary>
		/// Returns progress from 0 (just created) to 1 (expired).
		/// </summary>
		public float Progress => initial > 0f ? 1f - (remaining / initial) : 1f;

		/// <summary>
		/// Extends the remaining time.
		/// </summary>
		public void Extend(float seconds)
		{
			remaining += seconds;
			if (remaining > initial)
				initial = remaining;
		}

		/// <summary>
		/// Resets to initial duration.
		/// </summary>
		public void Reset()
		{
			remaining = initial;
		}
	}
}
