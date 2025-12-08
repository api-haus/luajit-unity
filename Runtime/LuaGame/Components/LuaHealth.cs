namespace LuaGame.Components
{
	using Unity.Entities;

	/// <summary>
	/// Health component for Lua-scripted entities.
	/// Tracks current and max health, and death state.
	/// </summary>
	public struct LuaHealth : IComponentData
	{
		public float current;
		public float max;
		public bool isDead;

		public static LuaHealth Create(float max)
		{
			return new LuaHealth
			{
				current = max,
				max = max,
				isDead = false,
			};
		}

		public void TakeDamage(float amount)
		{
			if (isDead)
				return;

			current -= amount;
			if (current <= 0)
			{
				current = 0;
				isDead = true;
			}
		}

		public void Heal(float amount)
		{
			if (isDead)
				return;

			current += amount;
			if (current > max)
				current = max;
		}

		public float HealthPercent => max > 0 ? current / max : 0;
	}
}
