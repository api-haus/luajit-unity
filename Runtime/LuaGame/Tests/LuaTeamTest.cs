namespace LuaGame.Tests
{
	using LuaGame.Components;
	using NUnit.Framework;

	[TestFixture]
	public class LuaTeamTest
	{
		[Test]
		public void Create_SetsDefaultMasks()
		{
			var team = LuaTeam.Create(5);

			Assert.AreEqual(5, team.teamId);
			Assert.AreEqual(1 << 5, team.allyMask);
			Assert.AreEqual(~(1 << 5), team.enemyMask);
		}

		[Test]
		public void Create_TeamZero()
		{
			var team = LuaTeam.Create(0);

			Assert.AreEqual(0, team.teamId);
			Assert.AreEqual(1, team.allyMask); // 1 << 0
			Assert.AreEqual(~1, team.enemyMask);
		}

		[Test]
		public void Create_WithCustomMasks()
		{
			var team = LuaTeam.Create(1, allyMask: 0b11, enemyMask: 0b1100);

			Assert.AreEqual(1, team.teamId);
			Assert.AreEqual(0b11, team.allyMask);
			Assert.AreEqual(0b1100, team.enemyMask);
		}

		[Test]
		public void Create_MaxTeamId()
		{
			var team = LuaTeam.Create(31);

			Assert.AreEqual(31, team.teamId);
			Assert.AreEqual(1 << 31, team.allyMask);
		}

		[Test]
		public void TeamBit_CalculatesCorrectly()
		{
			var team = LuaTeam.Create(3);

			Assert.AreEqual(1 << 3, team.TeamBit);
			Assert.AreEqual(8, team.TeamBit);
		}

		[Test]
		public void TeamBit_TeamZero()
		{
			var team = LuaTeam.Create(0);

			Assert.AreEqual(1, team.TeamBit);
		}

		[Test]
		public void IsAlly_SameTeam()
		{
			var team = LuaTeam.Create(2);

			Assert.IsTrue(team.IsAlly(2));
		}

		[Test]
		public void IsAlly_DifferentTeam()
		{
			var team = LuaTeam.Create(2);

			Assert.IsFalse(team.IsAlly(3));
		}

		[Test]
		public void IsAlly_MultipleAllies()
		{
			var team = LuaTeam.Create(1, allyMask: 0b111, enemyMask: 0); // Teams 0, 1, 2 are allies

			Assert.IsTrue(team.IsAlly(0));
			Assert.IsTrue(team.IsAlly(1));
			Assert.IsTrue(team.IsAlly(2));
			Assert.IsFalse(team.IsAlly(3));
		}

		[Test]
		public void IsEnemy_DifferentTeam()
		{
			var team = LuaTeam.Create(2);

			Assert.IsTrue(team.IsEnemy(3));
		}

		[Test]
		public void IsEnemy_SameTeam()
		{
			var team = LuaTeam.Create(2);

			Assert.IsFalse(team.IsEnemy(2));
		}

		[Test]
		public void IsEnemy_NeutralTeam()
		{
			var team = LuaTeam.Create(1);
			team.SetNeutral(3);

			Assert.IsFalse(team.IsEnemy(3)); // Neutral is not enemy
		}

		[Test]
		public void CanDamage_Enemy()
		{
			var team = LuaTeam.Create(1);

			Assert.IsTrue(team.CanDamage(2));
		}

		[Test]
		public void CanDamage_Ally()
		{
			var team = LuaTeam.Create(1);

			Assert.IsFalse(team.CanDamage(1));
		}

		[Test]
		public void CanDamage_Neutral()
		{
			var team = LuaTeam.Create(1);
			team.SetNeutral(3);

			// Neutral teams CAN be damaged (CanDamage = !IsAlly, not IsEnemy)
			// Neutral means neither ally nor enemy, so can still be damaged
			Assert.IsTrue(team.CanDamage(3));
		}

		[Test]
		public void AddAlly_UpdatesMasks()
		{
			var team = LuaTeam.Create(1);

			team.AddAlly(2);

			Assert.IsTrue(team.IsAlly(2));
			Assert.IsFalse(team.IsEnemy(2));
		}

		[Test]
		public void AddAlly_RemovesFromEnemyMask()
		{
			var team = LuaTeam.Create(1);
			Assert.IsTrue(team.IsEnemy(2)); // Initially enemy

			team.AddAlly(2);

			Assert.IsFalse(team.IsEnemy(2));
		}

		[Test]
		public void AddAlly_MultipleTeams()
		{
			var team = LuaTeam.Create(1);

			team.AddAlly(2);
			team.AddAlly(3);
			team.AddAlly(4);

			Assert.IsTrue(team.IsAlly(2));
			Assert.IsTrue(team.IsAlly(3));
			Assert.IsTrue(team.IsAlly(4));
		}

		[Test]
		public void AddEnemy_UpdatesMasks()
		{
			var team = LuaTeam.Create(1);
			team.AddAlly(2); // First make ally

			team.AddEnemy(2); // Then make enemy

			Assert.IsTrue(team.IsEnemy(2));
			Assert.IsFalse(team.IsAlly(2));
		}

		[Test]
		public void AddEnemy_RemovesFromAllyMask()
		{
			var team = LuaTeam.Create(1);
			team.AddAlly(2);
			Assert.IsTrue(team.IsAlly(2));

			team.AddEnemy(2);

			Assert.IsFalse(team.IsAlly(2));
		}

		[Test]
		public void SetNeutral_ClearsBothMasks()
		{
			var team = LuaTeam.Create(1);

			team.SetNeutral(2);

			Assert.IsFalse(team.IsAlly(2));
			Assert.IsFalse(team.IsEnemy(2));
		}

		[Test]
		public void SetNeutral_FromAlly()
		{
			var team = LuaTeam.Create(1);
			team.AddAlly(2);

			team.SetNeutral(2);

			Assert.IsFalse(team.IsAlly(2));
			Assert.IsFalse(team.IsEnemy(2));
		}

		[Test]
		public void SetNeutral_FromEnemy()
		{
			var team = LuaTeam.Create(1);
			Assert.IsTrue(team.IsEnemy(2)); // Default enemy

			team.SetNeutral(2);

			Assert.IsFalse(team.IsEnemy(2));
		}

		[Test]
		public void IsAlly_WithLuaTeam()
		{
			var teamA = LuaTeam.Create(1);
			var teamB = LuaTeam.Create(1);

			Assert.IsTrue(teamA.IsAlly(teamB));
		}

		[Test]
		public void IsAlly_WithDifferentLuaTeam()
		{
			var teamA = LuaTeam.Create(1);
			var teamB = LuaTeam.Create(2);

			Assert.IsFalse(teamA.IsAlly(teamB));
		}

		[Test]
		public void IsEnemy_WithLuaTeam()
		{
			var teamA = LuaTeam.Create(1);
			var teamB = LuaTeam.Create(2);

			Assert.IsTrue(teamA.IsEnemy(teamB));
		}

		[Test]
		public void IsEnemy_WithSameLuaTeam()
		{
			var teamA = LuaTeam.Create(1);
			var teamB = LuaTeam.Create(1);

			Assert.IsFalse(teamA.IsEnemy(teamB));
		}

		[Test]
		public void CanDamage_WithLuaTeam()
		{
			var teamA = LuaTeam.Create(1);
			var teamB = LuaTeam.Create(2);

			Assert.IsTrue(teamA.CanDamage(teamB));
			Assert.IsFalse(teamA.CanDamage(teamA));
		}

		[Test]
		public void CanDamage_MutualAllies()
		{
			var teamA = LuaTeam.Create(1);
			var teamB = LuaTeam.Create(2);

			teamA.AddAlly(2);
			teamB.AddAlly(1);

			Assert.IsFalse(teamA.CanDamage(teamB));
			Assert.IsFalse(teamB.CanDamage(teamA));
		}

		[Test]
		public void CanDamage_OneWayAlliance()
		{
			var teamA = LuaTeam.Create(1);
			var teamB = LuaTeam.Create(2);

			teamA.AddAlly(2); // A considers B ally
			// B still considers A enemy

			Assert.IsFalse(teamA.CanDamage(teamB)); // A can't damage B
			Assert.IsTrue(teamB.CanDamage(teamA)); // But B can damage A
		}

		[Test]
		public void AllyMask_DirectSet()
		{
			var team = LuaTeam.Create(1);
			team.allyMask = 0b11111;

			Assert.AreEqual(0b11111, team.allyMask);
			Assert.IsTrue(team.IsAlly(0));
			Assert.IsTrue(team.IsAlly(1));
			Assert.IsTrue(team.IsAlly(2));
			Assert.IsTrue(team.IsAlly(3));
			Assert.IsTrue(team.IsAlly(4));
		}

		[Test]
		public void EnemyMask_DirectSet()
		{
			var team = LuaTeam.Create(1);
			team.enemyMask = 0b1100;

			Assert.AreEqual(0b1100, team.enemyMask);
			Assert.IsTrue(team.IsEnemy(2));
			Assert.IsTrue(team.IsEnemy(3));
			Assert.IsFalse(team.IsEnemy(0));
			Assert.IsFalse(team.IsEnemy(1));
		}

		[Test]
		public void Relationship_Symmetric()
		{
			// Test that default relationships are symmetric
			var teamA = LuaTeam.Create(1);
			var teamB = LuaTeam.Create(2);

			Assert.AreEqual(teamA.IsAlly(teamB), teamB.IsAlly(teamA));
			Assert.AreEqual(teamA.IsEnemy(teamB), teamB.IsEnemy(teamA));
			Assert.AreEqual(teamA.CanDamage(teamB), teamB.CanDamage(teamA));
		}

		[Test]
		public void AllTeamsEnemy_Except_Self()
		{
			var team = LuaTeam.Create(5);

			// Self is ally
			Assert.IsTrue(team.IsAlly(5));
			Assert.IsFalse(team.IsEnemy(5));

			// Everyone else is enemy
			for (var i = 0; i < 32; i++)
			{
				if (i == 5)
					continue;
				Assert.IsTrue(team.IsEnemy(i), $"Team {i} should be enemy");
			}
		}
	}
}
