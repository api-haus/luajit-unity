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
	/// Integration test for entity creation via ecs.create_entity().
	/// Verifies ECB playback and ID resolution for deferred entities.
	/// </summary>
	public class LuaEntityCreationTest
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
			Assert.IsNotNull(m_ScriptingSystem, "LuaScriptingSystem should exist");
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			var query = m_EntityManager.CreateEntityQuery(typeof(LuaScript));
			m_EntityManager.DestroyEntity(query);

			var requestQuery = m_EntityManager.CreateEntityQuery(typeof(LuaScriptRequest));
			m_EntityManager.DestroyEntity(requestQuery);

			var idQuery = m_EntityManager.CreateEntityQuery(typeof(LuaEntityId));
			m_EntityManager.DestroyEntity(idQuery);

			yield return null;
		}

		[UnityTest]
		public IEnumerator EntityIdAssignedOnInitialization()
		{
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
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
			m_EntityManager.SetComponentData(entity, LocalTransform.FromPosition(0, 0, 0));

			Assert.AreEqual(
				0,
				m_EntityManager.GetComponentData<LuaEntityId>(entity).value,
				"Should have sentinel LuaEntityId (0) before init"
			);

			yield return null;
			yield return null;

			Assert.IsTrue(
				m_EntityManager.HasComponent<LuaEntityId>(entity),
				"Should have LuaEntityId after init"
			);

			var entityId = m_EntityManager.GetComponentData<LuaEntityId>(entity).value;
			Assert.Greater(entityId, 0, "Entity ID should be positive (not sentinel)");

			var resolvedEntity = m_ScriptingSystem.GetEntityFromId(entityId);
			Assert.AreEqual(entity, resolvedEntity, "ID should resolve back to same entity");
		}

		[UnityTest]
		public IEnumerator EntityIdLookupReturnsCorrectEntity()
		{
			var entity1 = CreateScriptedEntityWithId("fruit", new float3(1, 0, 0));
			var entity2 = CreateScriptedEntityWithId("fruit", new float3(2, 0, 0));
			var entity3 = CreateScriptedEntityWithId("fruit", new float3(3, 0, 0));

			yield return null;
			yield return null;

			var id1 = m_EntityManager.GetComponentData<LuaEntityId>(entity1).value;
			var id2 = m_EntityManager.GetComponentData<LuaEntityId>(entity2).value;
			var id3 = m_EntityManager.GetComponentData<LuaEntityId>(entity3).value;

			Assert.AreNotEqual(id1, id2, "IDs should be unique");
			Assert.AreNotEqual(id2, id3, "IDs should be unique");
			Assert.AreNotEqual(id1, id3, "IDs should be unique");

			Assert.AreEqual(entity1, m_ScriptingSystem.GetEntityFromId(id1));
			Assert.AreEqual(entity2, m_ScriptingSystem.GetEntityFromId(id2));
			Assert.AreEqual(entity3, m_ScriptingSystem.GetEntityFromId(id3));
		}

		[UnityTest]
		public IEnumerator InvalidIdReturnsNullEntity()
		{
			yield return null;

			var result = m_ScriptingSystem.GetEntityFromId(0);
			Assert.AreEqual(Entity.Null, result, "ID 0 should return null entity");

			result = m_ScriptingSystem.GetEntityFromId(-1);
			Assert.AreEqual(Entity.Null, result, "Negative ID should return null entity");

			result = m_ScriptingSystem.GetEntityFromId(999999);
			Assert.AreEqual(Entity.Null, result, "Non-existent ID should return null entity");
		}

		[UnityTest]
		public IEnumerator GetEntityIdFromEntityWorks()
		{
			var entity = CreateScriptedEntityWithId("fruit", float3.zero);

			yield return null;
			yield return null;

			var id = m_ScriptingSystem.GetEntityIdFromEntity(entity);
			Assert.Greater(id, 0, "Should return valid ID");

			var invalidId = m_ScriptingSystem.GetEntityIdFromEntity(Entity.Null);
			Assert.AreEqual(-1, invalidId, "Null entity should return -1");
		}

		Entity CreateScriptedEntityWithId(string scriptName, float3 position)
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
