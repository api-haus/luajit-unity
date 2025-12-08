namespace LuaGame.Components
{
	using Unity.Entities;

	/// <summary>
	/// Health component for Lua-scripted entities.
	/// Tracks current and max health, and death state.
	/// </summary>
	public struct LuaHealth : IComponentData
	{
		public float Current;
		public float Max;
		public bool IsDead;

		public static LuaHealth Create(float max)
		{
			return new LuaHealth
			{
				Current = max,
				Max = max,
				IsDead = false,
			};
		}

		public void TakeDamage(float amount)
		{
			if (IsDead)
				return;

			Current -= amount;
			if (Current <= 0)
			{
				Current = 0;
				IsDead = true;
			}
		}

		public void Heal(float amount)
		{
			if (IsDead)
				return;

			Current += amount;
			if (Current > Max)
				Current = Max;
		}

		public float HealthPercent => Max > 0 ? Current / Max : 0;
	}
}
