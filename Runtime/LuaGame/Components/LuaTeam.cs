namespace LuaGame.Components
{
	using Unity.Entities;

	/// <summary>
	/// Team/faction component for damage filtering.
	/// Uses bitmasks for efficient ally/enemy checks.
	/// Supports up to 32 teams (0-31).
	/// </summary>
	public struct LuaTeam : IComponentData
	{
		/// <summary>
		/// Team ID (0-31). Used to compute team bit: 1 << teamId.
		/// </summary>
		public int teamId;

		/// <summary>
		/// Bitmask of teams considered allies (no friendly fire).
		/// </summary>
		public int allyMask;

		/// <summary>
		/// Bitmask of teams considered enemies (can damage).
		/// </summary>
		public int enemyMask;

		/// <summary>
		/// Creates a team with default masks.
		/// By default: allies with same team, enemies with all others.
		/// </summary>
		public static LuaTeam Create(int teamId)
		{
			var teamBit = 1 << teamId;
			return new LuaTeam
			{
				teamId = teamId,
				allyMask = teamBit,
				enemyMask = ~teamBit,
			};
		}

		/// <summary>
		/// Creates a team with custom ally/enemy masks.
		/// </summary>
		public static LuaTeam Create(int teamId, int allyMask, int enemyMask)
		{
			return new LuaTeam
			{
				teamId = teamId,
				allyMask = allyMask,
				enemyMask = enemyMask,
			};
		}

		/// <summary>
		/// Gets the bit for this team: 1 << teamId.
		/// </summary>
		public int TeamBit => 1 << teamId;

		/// <summary>
		/// Returns true if other team is an ally.
		/// </summary>
		public bool IsAlly(int otherTeamId)
		{
			return (allyMask & (1 << otherTeamId)) != 0;
		}

		/// <summary>
		/// Returns true if other team is an enemy.
		/// </summary>
		public bool IsEnemy(int otherTeamId)
		{
			return (enemyMask & (1 << otherTeamId)) != 0;
		}

		/// <summary>
		/// Returns true if other team is an ally (by LuaTeam).
		/// </summary>
		public bool IsAlly(LuaTeam other)
		{
			return IsAlly(other.teamId);
		}

		/// <summary>
		/// Returns true if other team is an enemy (by LuaTeam).
		/// </summary>
		public bool IsEnemy(LuaTeam other)
		{
			return IsEnemy(other.teamId);
		}

		/// <summary>
		/// Returns true if can damage the other team (enemy or neutral).
		/// </summary>
		public bool CanDamage(int otherTeamId)
		{
			return !IsAlly(otherTeamId);
		}

		/// <summary>
		/// Returns true if can damage the other team (enemy or neutral).
		/// </summary>
		public bool CanDamage(LuaTeam other)
		{
			return CanDamage(other.teamId);
		}

		/// <summary>
		/// Adds a team to the ally mask.
		/// </summary>
		public void AddAlly(int otherTeamId)
		{
			allyMask |= 1 << otherTeamId;
			enemyMask &= ~(1 << otherTeamId);
		}

		/// <summary>
		/// Adds a team to the enemy mask.
		/// </summary>
		public void AddEnemy(int otherTeamId)
		{
			enemyMask |= 1 << otherTeamId;
			allyMask &= ~(1 << otherTeamId);
		}

		/// <summary>
		/// Sets team as neutral (neither ally nor enemy).
		/// </summary>
		public void SetNeutral(int otherTeamId)
		{
			var bit = 1 << otherTeamId;
			allyMask &= ~bit;
			enemyMask &= ~bit;
		}
	}
}
