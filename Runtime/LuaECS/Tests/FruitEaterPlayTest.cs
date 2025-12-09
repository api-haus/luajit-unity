namespace LuaECS.Tests
{
	using System.Collections;
	using Components;
	using Core;
	using NUnit.Framework;
	using Unity.Entities;
	using Unity.Transforms;
	using UnityEngine;
	using UnityEngine.TestTools;

	public class FruitEaterPlayTest
	{
		World m_World;
		EntityManager m_EntityManager;
		GameObject m_VisualizerGO;
		GameObject m_CameraGO;

		const int GRID_SIZE = 10;
		const int AGENT_COUNT = 5;
		const int INITIAL_FRUIT_COUNT = 20;
		const float TEST_DURATION = 5f;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			m_World = World.DefaultGameObjectInjectionWorld;
			m_EntityManager = m_World.EntityManager;

			LuaTestUtilities.GetOrCreateTestVM();

			yield return null;

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

			m_VisualizerGO = new GameObject("FruitEaterVisualizer");
			m_VisualizerGO.AddComponent<FruitEaterVisualizer>();

			yield return null;
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			if (m_CameraGO != null)
				Object.Destroy(m_CameraGO);

			if (m_VisualizerGO != null)
				Object.Destroy(m_VisualizerGO);

			var query = m_EntityManager.CreateEntityQuery(typeof(LuaScript));
			m_EntityManager.DestroyEntity(query);

			var requestQuery = m_EntityManager.CreateEntityQuery(typeof(LuaScriptRequest));
			m_EntityManager.DestroyEntity(requestQuery);

			yield return null;
		}

		[UnityTest]
		public IEnumerator AgentsEatFruitsOnGrid()
		{
			SpawnAgents();
			SpawnFruits(INITIAL_FRUIT_COUNT);

			var elapsed = 0f;
			var fruitSpawnTimer = 0f;

			var agentQuery = m_EntityManager.CreateEntityQuery(
				ComponentType.ReadOnly<LuaScript>(),
				ComponentType.ReadOnly<AgentTag>()
			);

			var fruitQuery = m_EntityManager.CreateEntityQuery(
				ComponentType.ReadOnly<LuaScript>(),
				ComponentType.ReadOnly<FruitTag>()
			);

			while (elapsed < TEST_DURATION)
			{
				elapsed += Time.deltaTime;
				fruitSpawnTimer += Time.deltaTime;

				if (fruitSpawnTimer > 2f)
				{
					var currentFruits = fruitQuery.CalculateEntityCount();
					if (currentFruits < 15)
					{
						SpawnFruits(5);
					}
					fruitSpawnTimer = 0f;
				}

				var agentCount = agentQuery.CalculateEntityCount();
				var fruitCount = fruitQuery.CalculateEntityCount();

				yield return null;
			}

			Assert.Pass($"Test completed. Agents ate fruits for {TEST_DURATION} seconds.");
		}

		void SpawnAgents()
		{
			for (var i = 0; i < AGENT_COUNT; i++)
			{
				var x = Random.Range(-GRID_SIZE / 2f, GRID_SIZE / 2f);
				var z = Random.Range(-GRID_SIZE / 2f, GRID_SIZE / 2f);

				var entity = m_EntityManager.CreateEntity(typeof(LocalTransform), typeof(AgentTag));

				var requests = m_EntityManager.AddBuffer<LuaScriptRequest>(entity);
				requests.Add(
					new LuaScriptRequest
					{
						scriptName = "fruit_eater",
						requestHash = LuaScriptPathUtility.HashScriptName("fruit_eater"),
						fulfilled = false,
					}
				);

				m_EntityManager.AddBuffer<LuaEvent>(entity);
				m_EntityManager.AddComponentData(entity, new LuaEntityId { value = 0 });
				m_EntityManager.SetComponentData(entity, LocalTransform.FromPosition(x, 0, z));
			}

			Unity.Logging.Log.Info($"[FruitTest] Spawned {AGENT_COUNT} agents");
		}

		void SpawnFruits(int count)
		{
			for (var i = 0; i < count; i++)
			{
				var x = Random.Range(-GRID_SIZE / 2f, GRID_SIZE / 2f);
				var z = Random.Range(-GRID_SIZE / 2f, GRID_SIZE / 2f);

				var entity = m_EntityManager.CreateEntity(typeof(LocalTransform), typeof(FruitTag));

				var requests = m_EntityManager.AddBuffer<LuaScriptRequest>(entity);
				requests.Add(
					new LuaScriptRequest
					{
						scriptName = "fruit",
						requestHash = LuaScriptPathUtility.HashScriptName("fruit"),
						fulfilled = false,
					}
				);

				m_EntityManager.AddBuffer<LuaEvent>(entity);
				m_EntityManager.AddComponentData(entity, new LuaEntityId { value = 0 });
				m_EntityManager.SetComponentData(entity, LocalTransform.FromPosition(x, 0, z));
			}

			Unity.Logging.Log.Info($"[FruitTest] Spawned {count} fruits");
		}
	}
}
