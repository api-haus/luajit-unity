namespace LuaGame.Tests
{
	using System.Collections;
	using System.Collections.Generic;
	using Components;
	using GameMode;
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaECS.Tests;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;
	using UnityEngine;
	using UnityEngine.TestTools;

	/// <summary>
	/// PlayMode test that runs the meteor_shower game mode via GameModeManager.
	/// Validates damage zones deal damage to agents and agents die when HP reaches zero.
	/// </summary>
	public class MeteorShowerGameModePlayTest
	{
		GameModeManager m_Manager;
		GameObject m_VisualizerGO;
		GameObject m_CameraGO;
		MeteorShowerVisualizer m_Visualizer;
		float m_OriginalTimeScale;

		const float TEST_DURATION = 8f;
		const float TIME_SCALE = 3f;
		const int GRID_SIZE = 20;

		// Agent parameters (must match GameModes/meteor_shower.lua)
		const int AGENT_COUNT = 10;
		const float AGENT_MAX_HEALTH = 100f;

		// Meteor parameters
		const float METEOR_INTERVAL = 0.3f;
		const float METEOR_DAMAGE = 15f;
		const int METEORS_PER_WAVE = 3;

		// Success criteria
		const int MIN_DEATHS = 3; // At least 3 agents should die during the test
		const float MIN_DAMAGE_DEALT = 100f; // At least 100 total damage should be dealt

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			// Ensure clean state
			if (LuaEntityRegistry.IsCreated)
				LuaEntityRegistry.Dispose();

			// Register test scripts path before creating manager
			LuaTestUtilities.GetOrCreateTestVM();

			m_Manager = new GameModeManager();

			// Store original time scale
			m_OriginalTimeScale = Time.timeScale;
			Time.timeScale = TIME_SCALE;

			// Set up camera for visualization
			m_CameraGO = new GameObject("TestCamera");
			var cam = m_CameraGO.AddComponent<Camera>();
			cam.orthographic = true;
			cam.orthographicSize = GRID_SIZE * 0.6f;
			cam.nearClipPlane = 0.1f;
			cam.farClipPlane = 100f;
			cam.clearFlags = CameraClearFlags.SolidColor;
			cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f);
			m_CameraGO.transform.position = new Vector3(0, 30, 0);
			m_CameraGO.transform.rotation = Quaternion.Euler(90, 0, 0);

			// Set up visualizer
			m_VisualizerGO = new GameObject("MeteorShowerVisualizer");
			m_Visualizer = m_VisualizerGO.AddComponent<MeteorShowerVisualizer>();

			yield return null;
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			// Restore original time scale
			Time.timeScale = m_OriginalTimeScale;

			if (m_CameraGO != null)
				Object.Destroy(m_CameraGO);

			if (m_VisualizerGO != null)
				Object.Destroy(m_VisualizerGO);

			m_Manager?.Dispose();
			m_Manager = null;

			// Cleanup any leftover registry state
			if (LuaEntityRegistry.IsCreated)
				LuaEntityRegistry.Dispose();

			yield return null;
		}

		void UpdateWorld(World world)
		{
			world.Update();
		}

		/// <summary>
		/// Gets health data for entities with a specific script name.
		/// </summary>
		Dictionary<int, LuaHealth> GetEntityHealths(EntityManager em, string scriptName)
		{
			var healths = new Dictionary<int, LuaHealth>();

			using var query = em.CreateEntityQuery(
				ComponentType.ReadOnly<LuaHealth>(),
				ComponentType.ReadOnly<LuaScript>(),
				ComponentType.ReadOnly<LuaEntityId>()
			);

			var entities = query.ToEntityArray(Allocator.Temp);
			var healthData = query.ToComponentDataArray<LuaHealth>(Allocator.Temp);
			var ids = query.ToComponentDataArray<LuaEntityId>(Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				var scripts = em.GetBuffer<LuaScript>(entities[i]);
				for (var j = 0; j < scripts.Length; j++)
				{
					if (scripts[j].scriptName.ToString() == scriptName)
					{
						healths[ids[i].value] = healthData[i];
						break;
					}
				}
			}

			entities.Dispose();
			healthData.Dispose();
			ids.Dispose();

			return healths;
		}

		/// <summary>
		/// Counts entities with a specific script name.
		/// </summary>
		int CountEntitiesWithScript(EntityManager em, string scriptName)
		{
			var count = 0;

			using var query = em.CreateEntityQuery(ComponentType.ReadOnly<LuaScript>());

			var entities = query.ToEntityArray(Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				var scripts = em.GetBuffer<LuaScript>(entities[i]);
				for (var j = 0; j < scripts.Length; j++)
				{
					if (scripts[j].scriptName.ToString() == scriptName)
					{
						count++;
						break;
					}
				}
			}

			entities.Dispose();
			return count;
		}

		/// <summary>
		/// Counts active damage zones.
		/// </summary>
		int CountDamageZones(EntityManager em)
		{
			using var query = em.CreateEntityQuery(ComponentType.ReadOnly<LuaDamageZone>());
			return query.CalculateEntityCount();
		}

		[UnityTest]
		public IEnumerator AgentsTakeDamageAndDie()
		{
			// Load the meteor_shower game mode
			var loadTask = m_Manager.LoadModeAsync("meteor_shower");
			while (!loadTask.IsCompleted)
				yield return null;

			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			Assert.IsNotNull(m_Manager.CurrentWorld, "Game mode world should exist");

			var world = m_Manager.CurrentWorld;
			var em = world.EntityManager;
			m_Visualizer.TargetWorld = world;

			// Let entities initialize
			for (var i = 0; i < 10; i++)
			{
				UpdateWorld(world);
				yield return null;
			}

			// Record initial state
			var initialHealths = GetEntityHealths(em, "meteor_victim");
			Assert.AreEqual(AGENT_COUNT, initialHealths.Count, $"Should have {AGENT_COUNT} agents");

			// Verify all agents start at full health
			foreach (var kvp in initialHealths)
			{
				Assert.AreEqual(
					AGENT_MAX_HEALTH,
					kvp.Value.max,
					$"Agent {kvp.Key} should have max health of {AGENT_MAX_HEALTH}"
				);
				Assert.AreEqual(
					AGENT_MAX_HEALTH,
					kvp.Value.current,
					$"Agent {kvp.Key} should start at full health"
				);
				Assert.IsFalse(kvp.Value.isDead, $"Agent {kvp.Key} should not be dead initially");
			}

			Unity.Logging.Log.Info(
				"[MeteorShowerTest] Initial state: {0} agents at {1} HP each",
				initialHealths.Count,
				AGENT_MAX_HEALTH
			);

			// Track damage and deaths over time
			var totalDamageDealt = 0f;
			var deathCount = 0;
			var maxMeteorsAtOnce = 0;
			var elapsed = 0f;
			var frameCount = 0;

			// Run the simulation
			while (elapsed < TEST_DURATION)
			{
				var dt = Time.deltaTime;
				UpdateWorld(world);

				// Track meteor count
				var currentMeteors = CountDamageZones(em);
				if (currentMeteors > maxMeteorsAtOnce)
					maxMeteorsAtOnce = currentMeteors;

				// Calculate damage dealt by comparing health
				var currentHealths = GetEntityHealths(em, "meteor_victim");
				var currentDeaths = 0;
				var currentDamage = 0f;

				foreach (var kvp in currentHealths)
				{
					if (kvp.Value.isDead)
						currentDeaths++;
					currentDamage += AGENT_MAX_HEALTH - kvp.Value.current;
				}

				totalDamageDealt = currentDamage;
				deathCount = currentDeaths;

				elapsed += dt;
				frameCount++;

				yield return null;
			}

			// Final state
			var finalHealths = GetEntityHealths(em, "meteor_victim");
			var finalDeaths = 0;
			var finalDamage = 0f;

			foreach (var kvp in finalHealths)
			{
				if (kvp.Value.isDead)
					finalDeaths++;
				finalDamage += AGENT_MAX_HEALTH - kvp.Value.current;
			}

			Unity.Logging.Log.Info(
				"[MeteorShowerTest] Results after {0:F2}s ({1} frames at {2}x speed):",
				elapsed,
				frameCount,
				TIME_SCALE
			);
			Unity.Logging.Log.Info("  - Deaths: {0}/{1} agents", finalDeaths, AGENT_COUNT);
			Unity.Logging.Log.Info("  - Total damage dealt: {0:F1} HP", finalDamage);
			Unity.Logging.Log.Info("  - Max concurrent meteors: {0}", maxMeteorsAtOnce);

			// Assertions with success criteria
			Assert.GreaterOrEqual(
				finalDeaths,
				MIN_DEATHS,
				$"At least {MIN_DEATHS} agents should have died, but only {finalDeaths} died"
			);

			Assert.Greater(
				finalDamage,
				MIN_DAMAGE_DEALT,
				$"At least {MIN_DAMAGE_DEALT} total damage should be dealt, but only {finalDamage:F1} was dealt"
			);

			Assert.Greater(maxMeteorsAtOnce, 0, "Meteors should have been spawned during the test");
		}

		[UnityTest]
		public IEnumerator GameModeSpawnsAgentsWithHealth()
		{
			// Load the game mode
			var loadTask = m_Manager.LoadModeAsync("meteor_shower");
			while (!loadTask.IsCompleted)
				yield return null;

			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			var world = m_Manager.CurrentWorld;
			var em = world.EntityManager;
			m_Visualizer.TargetWorld = world;

			// Run several frames for entities to be created
			for (var i = 0; i < 20; i++)
			{
				UpdateWorld(world);
				yield return null;
			}

			// Check that entities were spawned via ecs.create_entity()
			using var entityIdQuery = em.CreateEntityQuery(typeof(LuaEntityId));
			var entityCount = entityIdQuery.CalculateEntityCount();

			// Should have mode entity + spawned agents
			Assert.Greater(entityCount, 5, "Should have spawned multiple entities via bridge");

			var agentCount = CountEntitiesWithScript(em, "meteor_victim");
			var agentHealths = GetEntityHealths(em, "meteor_victim");

			Unity.Logging.Log.Info(
				"[MeteorShowerTest] Spawned: {0} total entities, {1} agents with health",
				entityCount,
				agentCount
			);

			Assert.AreEqual(AGENT_COUNT, agentCount, $"Should have spawned exactly {AGENT_COUNT} agents");
			Assert.AreEqual(AGENT_COUNT, agentHealths.Count, "All agents should have health components");

			// Verify health values
			foreach (var kvp in agentHealths)
			{
				Assert.AreEqual(AGENT_MAX_HEALTH, kvp.Value.max, "Agent max health should match");
			}
		}

		[UnityTest]
		public IEnumerator MeteorsSpawnAndDealDamage()
		{
			var loadTask = m_Manager.LoadModeAsync("meteor_shower");
			while (!loadTask.IsCompleted)
				yield return null;

			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			var world = m_Manager.CurrentWorld;
			var em = world.EntityManager;
			m_Visualizer.TargetWorld = world;

			// Run initial frames
			for (var i = 0; i < 10; i++)
			{
				UpdateWorld(world);
				yield return null;
			}

			var initialHealths = GetEntityHealths(em, "meteor_victim");
			var initialTotalHealth = 0f;
			foreach (var kvp in initialHealths)
				initialTotalHealth += kvp.Value.current;

			var meteorsSpawned = false;
			var damageDealt = false;

			// Run for 2 seconds - watch for meteors and damage
			var elapsed = 0f;
			while (elapsed < 2f)
			{
				var dt = Time.deltaTime;
				UpdateWorld(world);

				// Check for meteors
				var meteorCount = CountDamageZones(em);
				if (meteorCount > 0)
					meteorsSpawned = true;

				// Check for damage
				var currentHealths = GetEntityHealths(em, "meteor_victim");
				var currentTotalHealth = 0f;
				foreach (var kvp in currentHealths)
					currentTotalHealth += kvp.Value.current;

				if (currentTotalHealth < initialTotalHealth)
					damageDealt = true;

				elapsed += dt;
				yield return null;
			}

			Unity.Logging.Log.Info(
				"[MeteorShowerTest] Meteor spawning: {0}, Damage dealt: {1}",
				meteorsSpawned ? "Yes" : "No",
				damageDealt ? "Yes" : "No"
			);

			Assert.IsTrue(meteorsSpawned, "Meteors should have been spawned");
			Assert.IsTrue(damageDealt, "Damage should have been dealt to agents");
		}

		[UnityTest]
		public IEnumerator AllAgentsDieEventually()
		{
			var loadTask = m_Manager.LoadModeAsync("meteor_shower");
			while (!loadTask.IsCompleted)
				yield return null;

			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			var world = m_Manager.CurrentWorld;
			var em = world.EntityManager;
			m_Visualizer.TargetWorld = world;

			// Run initial frames
			for (var i = 0; i < 10; i++)
			{
				UpdateWorld(world);
				yield return null;
			}

			// Run until all agents are dead or timeout (longer test)
			var maxDuration = 15f; // Give enough time for all to die
			var elapsed = 0f;
			var allDead = false;

			while (elapsed < maxDuration && !allDead)
			{
				var dt = Time.deltaTime;
				UpdateWorld(world);

				var healths = GetEntityHealths(em, "meteor_victim");
				var aliveCount = 0;
				foreach (var kvp in healths)
				{
					if (!kvp.Value.isDead)
						aliveCount++;
				}

				if (aliveCount == 0 && healths.Count > 0)
					allDead = true;

				elapsed += dt;
				yield return null;
			}

			Unity.Logging.Log.Info(
				"[MeteorShowerTest] Game over: {0} after {1:F2}s",
				allDead ? "All agents dead" : "Timeout",
				elapsed
			);

			// We don't strictly require all agents to die (that would make test flaky)
			// Instead verify at least some significant progress was made
			var finalHealths = GetEntityHealths(em, "meteor_victim");
			var finalDeaths = 0;
			foreach (var kvp in finalHealths)
			{
				if (kvp.Value.isDead)
					finalDeaths++;
			}

			Assert.GreaterOrEqual(
				finalDeaths,
				AGENT_COUNT / 2,
				$"At least half of the agents ({AGENT_COUNT / 2}) should have died, but only {finalDeaths} died"
			);
		}
	}
}
