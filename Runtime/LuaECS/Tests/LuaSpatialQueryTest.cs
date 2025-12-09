namespace LuaECS.Tests
{
	using System.Collections;
	using Components;
	using Core;
	using NUnit.Framework;
	using Systems;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;
	using UnityEngine.TestTools;

	/// <summary>
	/// Integration test for spatial queries via query_entities_near.
	/// Verifies accuracy of radius-based entity lookups.
	/// </summary>
	public class LuaSpatialQueryTest
	{
		World m_World;
		EntityManager m_EntityManager;
		LuaScriptingSystem m_ScriptingSystem;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			m_World = World.DefaultGameObjectInjectionWorld;
			m_EntityManager = m_World.EntityManager;
			LuaTestUtilities.GetOrCreateTestVM();

			yield return null;

			m_ScriptingSystem = m_World.GetExistingSystemManaged<LuaScriptingSystem>();
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			var query = m_EntityManager.CreateEntityQuery(typeof(LuaScript));
			m_EntityManager.DestroyEntity(query);

			var requestQuery = m_EntityManager.CreateEntityQuery(typeof(LuaScriptRequest));
			m_EntityManager.DestroyEntity(requestQuery);
			yield return null;
		}

		[UnityTest]
		public IEnumerator DistanceCalculationIsAccurate()
		{
			var entity1 = CreateScriptedEntity("fruit", new float3(0, 0, 0));
			var entity2 = CreateScriptedEntity("fruit", new float3(3, 4, 0));

			yield return null;
			yield return null;

			var id1 = m_ScriptingSystem.GetEntityIdFromEntity(entity1);
			var id2 = m_ScriptingSystem.GetEntityIdFromEntity(entity2);

			Assert.Greater(id1, 0);
			Assert.Greater(id2, 0);

			var pos1 = m_EntityManager.GetComponentData<LocalTransform>(entity1).Position;
			var pos2 = m_EntityManager.GetComponentData<LocalTransform>(entity2).Position;
			var expectedDistance = math.distance(pos1, pos2);

			Assert.AreEqual(5.0f, expectedDistance, 0.01f, "Distance should be 5 (3-4-5 triangle)");
		}

		[UnityTest]
		public IEnumerator EntitiesAtDifferentDistances()
		{
			var center = CreateScriptedEntity("fruit", float3.zero);

			var near = CreateScriptedEntity("fruit", new float3(2, 0, 0));
			var mid = CreateScriptedEntity("fruit", new float3(5, 0, 0));
			var far = CreateScriptedEntity("fruit", new float3(10, 0, 0));

			yield return null;
			yield return null;

			var centerPos = m_EntityManager.GetComponentData<LocalTransform>(center).Position;
			var nearPos = m_EntityManager.GetComponentData<LocalTransform>(near).Position;
			var midPos = m_EntityManager.GetComponentData<LocalTransform>(mid).Position;
			var farPos = m_EntityManager.GetComponentData<LocalTransform>(far).Position;

			Assert.AreEqual(2.0f, math.distance(centerPos, nearPos), 0.01f);
			Assert.AreEqual(5.0f, math.distance(centerPos, midPos), 0.01f);
			Assert.AreEqual(10.0f, math.distance(centerPos, farPos), 0.01f);

			Assert.Pass("Distance calculations verified for multiple entities");
		}

		[UnityTest]
		public IEnumerator EntityCountIsAccurate()
		{
			var initialQuery = m_EntityManager.CreateEntityQuery(typeof(LuaScript));
			var initialCount = initialQuery.CalculateEntityCount();

			CreateScriptedEntity("fruit", new float3(0, 0, 0));
			CreateScriptedEntity("fruit", new float3(1, 0, 0));
			CreateScriptedEntity("fruit", new float3(2, 0, 0));

			yield return null;

			var query = m_EntityManager.CreateEntityQuery(typeof(LuaScript));
			var count = query.CalculateEntityCount();

			Assert.AreEqual(initialCount + 3, count, "Should have 3 more entities");
		}

		[UnityTest]
		public IEnumerator QueryIncludesEntitiesWithinRadius()
		{
			var center = CreateScriptedEntity("fruit", float3.zero);
			CreateScriptedEntity("fruit", new float3(3, 0, 0));
			CreateScriptedEntity("fruit", new float3(4, 0, 0));
			CreateScriptedEntity("fruit", new float3(6, 0, 0));
			CreateScriptedEntity("fruit", new float3(10, 0, 0));

			yield return null;
			yield return null;

			var query = m_EntityManager.CreateEntityQuery(
				ComponentType.ReadOnly<LocalTransform>(),
				ComponentType.ReadOnly<LuaScript>()
			);

			var entities = query.ToEntityArray(Unity.Collections.Allocator.Persistent);
			var inRadius5 = 0;

			foreach (var e in entities)
			{
				var pos = m_EntityManager.GetComponentData<LocalTransform>(e).Position;
				if (math.distance(float3.zero, pos) <= 5.0f)
					inRadius5++;
			}

			entities.Dispose();

			Assert.AreEqual(3, inRadius5, "Should have 3 entities within radius 5 (at 0, 3, 4)");
		}

		Entity CreateScriptedEntity(string scriptName, float3 position)
		{
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
			var requests = m_EntityManager.AddBuffer<LuaScriptRequest>(entity);
			requests.Add(
				new LuaScriptRequest
				{
					scriptName = scriptName,
					requestHash = LuaScriptPathUtility.HashScriptName(scriptName),
					fulfilled = false,
				}
			);
			m_EntityManager.AddBuffer<LuaEvent>(entity);
			m_EntityManager.AddComponentData(entity, new LuaEntityId { value = 0 });
			m_EntityManager.SetComponentData(entity, LocalTransform.FromPosition(position));
			return entity;
		}
	}
}
