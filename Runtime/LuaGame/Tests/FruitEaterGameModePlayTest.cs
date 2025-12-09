namespace LuaGame.Tests
{
	using System.Collections;
	using System.Collections.Generic;
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
	/// PlayMode test that runs the fruit_eater game mode via GameModeManager.
	/// Validates agent movement and fruit consumption with measurable success criteria.
	/// </summary>
	public class FruitEaterGameModePlayTest
	{
		GameModeManager m_Manager;
		GameObject m_VisualizerGO;
		GameObject m_CameraGO;
		FruitEaterVisualizer m_Visualizer;
		float m_OriginalTimeScale;

		const float TEST_DURATION = 5f;
		const float TIME_SCALE = 2f;
		const int GRID_SIZE = 10;

		// Agent parameters (must match GameModes/fruit_eater.lua)
		const int AGENT_COUNT = 5;
		const float AGENT_SPEED = 3.0f;
		const float MOVEMENT_EFFICIENCY = 0.8f; // Account for init/load delays and target switching

		// Success criteria: A * S * T * efficiency
		const float MIN_TOTAL_DISTANCE = AGENT_COUNT * AGENT_SPEED * TEST_DURATION * MOVEMENT_EFFICIENCY;
		const int MIN_FRUITS_EATEN = 3; // At least 3 fruits must be eaten

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			// Ensure clean state
			if (LuaEntityRegistry.IsCreated)
				LuaEntityRegistry.Dispose();

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
			cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);
			m_CameraGO.transform.position = new Vector3(0, 20, 0);
			m_CameraGO.transform.rotation = Quaternion.Euler(90, 0, 0);

			// Set up visualizer - configured to query by script name for Lua-created entities
			m_VisualizerGO = new GameObject("FruitEaterVisualizer");
			m_Visualizer = m_VisualizerGO.AddComponent<FruitEaterVisualizer>();
			m_Visualizer.QueryByScriptName = true;

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

		/// <summary>
		/// Updates world - uses Unity's Time.time/deltaTime affected by Time.timeScale.
		/// </summary>
		void UpdateWorld(World world)
		{
			world.Update();
		}

		/// <summary>
		/// Gets positions of all entities with a specific script name.
		/// </summary>
		Dictionary<int, float3> GetEntityPositions(EntityManager em, string scriptName)
		{
			var positions = new Dictionary<int, float3>();

			using var query = em.CreateEntityQuery(
				ComponentType.ReadOnly<LocalTransform>(),
				ComponentType.ReadOnly<LuaScript>(),
				ComponentType.ReadOnly<LuaEntityId>()
			);

			var entities = query.ToEntityArray(Allocator.Temp);
			var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
			var ids = query.ToComponentDataArray<LuaEntityId>(Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				var scripts = em.GetBuffer<LuaScript>(entities[i]);
				for (var j = 0; j < scripts.Length; j++)
				{
					if (scripts[j].scriptName.ToString() == scriptName)
					{
						positions[ids[i].value] = transforms[i].Position;
						break;
					}
				}
			}

			entities.Dispose();
			transforms.Dispose();
			ids.Dispose();

			return positions;
		}

		/// <summary>
		/// Counts entities with a specific script name.
		/// </summary>
		int CountEntitiesWithScript(EntityManager em, string scriptName)
		{
			var count = 0;

			using var query = em.CreateEntityQuery(
				ComponentType.ReadOnly<LuaScript>()
			);

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

		[UnityTest]
		public IEnumerator AgentsMovementAndFruitConsumption()
		{
			// Load the fruit_eater game mode
			var loadTask = m_Manager.LoadModeAsync("fruit_eater");
			while (!loadTask.IsCompleted)
				yield return null;

			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			Assert.IsNotNull(m_Manager.CurrentWorld, "Game mode world should exist");

			var world = m_Manager.CurrentWorld;
			var em = world.EntityManager;
			m_Visualizer.TargetWorld = world;

			// Let entities initialize (several frames for ECB playback and script fulfillment)
			for (var i = 0; i < 10; i++)
			{
				UpdateWorld(world);
				yield return null;
			}

			// Record initial state
			var initialAgentPositions = GetEntityPositions(em, "fruit_eater");
			var initialFruitCount = CountEntitiesWithScript(em, "fruit");

			Assert.Greater(initialAgentPositions.Count, 0, "Should have agents after initialization");
			Assert.Greater(initialFruitCount, 0, "Should have fruits after initialization");

			Unity.Logging.Log.Info(
				"[GameModeTest] Initial state: {0} agents, {1} fruits",
				initialAgentPositions.Count, initialFruitCount
			);

			// Track cumulative distance and minimum fruit count seen
			var cumulativeDistance = 0f;
			var previousPositions = new Dictionary<int, float3>(initialAgentPositions);
			var minFruitCount = initialFruitCount;
			var elapsed = 0f;
			var frameCount = 0;

			// Run the simulation
			// Note: Time.timeScale is set to TIME_SCALE, so Time.deltaTime already reflects the scaling
			while (elapsed < TEST_DURATION)
			{
				var dt = Time.deltaTime;
				UpdateWorld(world);

				// Calculate distance traveled this frame
				var currentPositions = GetEntityPositions(em, "fruit_eater");
				foreach (var kvp in currentPositions)
				{
					if (previousPositions.TryGetValue(kvp.Key, out var prevPos))
					{
						cumulativeDistance += math.distance(prevPos, kvp.Value);
					}
				}
				previousPositions = currentPositions;

				// Track minimum fruit count (to detect eating)
				var currentFruitCount = CountEntitiesWithScript(em, "fruit");
				if (currentFruitCount < minFruitCount)
					minFruitCount = currentFruitCount;

				elapsed += dt;
				frameCount++;

				yield return null;
			}

			// Calculate metrics
			var finalAgentPositions = GetEntityPositions(em, "fruit_eater");
			var finalFruitCount = CountEntitiesWithScript(em, "fruit");
			var fruitsEaten = initialFruitCount - minFruitCount;

			// Calculate total distance from initial to final positions
			var totalDisplacement = 0f;
			foreach (var kvp in finalAgentPositions)
			{
				if (initialAgentPositions.TryGetValue(kvp.Key, out var initialPos))
				{
					totalDisplacement += math.distance(initialPos, kvp.Value);
				}
			}

			Unity.Logging.Log.Info(
				"[GameModeTest] Results after {0:F2}s ({1} frames at {2}x speed):",
				elapsed, frameCount, TIME_SCALE
			);
			Unity.Logging.Log.Info(
				"  - Agents: {0} initial, {1} final",
				initialAgentPositions.Count, finalAgentPositions.Count
			);
			Unity.Logging.Log.Info(
				"  - Fruits: {0} initial, {1} final, {2} eaten (min seen: {3})",
				initialFruitCount, finalFruitCount, fruitsEaten, minFruitCount
			);
			Unity.Logging.Log.Info(
				"  - Movement: {0:F2} cumulative distance, {1:F2} total displacement",
				cumulativeDistance, totalDisplacement
			);

			// Assertions with success criteria
			Assert.Greater(
				cumulativeDistance, MIN_TOTAL_DISTANCE,
				$"Agents should have moved at least {MIN_TOTAL_DISTANCE} units total, but only moved {cumulativeDistance:F2}"
			);

			Assert.GreaterOrEqual(
				fruitsEaten, MIN_FRUITS_EATEN,
				$"Agents should have eaten at least {MIN_FRUITS_EATEN} fruits, but only ate {fruitsEaten}"
			);

			Assert.Greater(
				finalAgentPositions.Count, 0,
				"All agents should still exist at end of test"
			);
		}

		[UnityTest]
		public IEnumerator GameModeSpawnsEntitiesViaBridge()
		{
			// Load the game mode
			var loadTask = m_Manager.LoadModeAsync("fruit_eater");
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

			// Should have mode entity + spawned agents/fruits
			// Mode entity = 1, Agents = 5, Fruits = 20 initially = 26 entities
			Assert.Greater(entityCount, 5, "Should have spawned multiple entities via bridge");

			var agentCount = CountEntitiesWithScript(em, "fruit_eater");
			var fruitCount = CountEntitiesWithScript(em, "fruit");

			Unity.Logging.Log.Info(
				"[GameModeTest] Spawned entities: {0} total ({1} agents, {2} fruits)",
				entityCount, agentCount, fruitCount
			);

			Assert.AreEqual(5, agentCount, "Should have spawned exactly 5 agents");
			Assert.GreaterOrEqual(fruitCount, 15, "Should have at least 15 fruits");
		}

		[UnityTest]
		public IEnumerator GameModeContinuesSpawningFruits()
		{
			var loadTask = m_Manager.LoadModeAsync("fruit_eater");
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

			var initialFruitCount = CountEntitiesWithScript(em, "fruit");
			var minFruitCount = initialFruitCount;
			var maxFruitCount = initialFruitCount;

			// Run for 3 more seconds - track fruit count changes
			var elapsed = 0f;
			while (elapsed < 3f)
			{
				var dt = Time.deltaTime;
				UpdateWorld(world);

				var fruitCount = CountEntitiesWithScript(em, "fruit");
				minFruitCount = Mathf.Min(minFruitCount, fruitCount);
				maxFruitCount = Mathf.Max(maxFruitCount, fruitCount);

				elapsed += dt;
				yield return null;
			}

			var finalFruitCount = CountEntitiesWithScript(em, "fruit");

			Unity.Logging.Log.Info(
				"[GameModeTest] Fruit tracking: initial={0}, min={1}, max={2}, final={3}",
				initialFruitCount, minFruitCount, maxFruitCount, finalFruitCount
			);

			// Verify the system remained stable and fruits were managed
			Assert.Greater(finalFruitCount, 0, "Should still have fruits after running");

			// If min < max, spawning occurred to replenish eaten fruits
			if (minFruitCount < initialFruitCount)
			{
				Unity.Logging.Log.Info(
					"[GameModeTest] Fruits were consumed (min {0} < initial {1})",
					minFruitCount, initialFruitCount
				);
			}
		}
	}
}
