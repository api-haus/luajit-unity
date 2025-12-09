namespace LuaGame.Tests
{
	using System.Collections;
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaECS.Systems.Support;
	using LuaECS.Tests;
	using NUnit.Framework;
	using Unity.Entities;
	using Unity.Transforms;
	using UnityEngine.TestTools;

	/// <summary>
	/// Integration tests for the entity destruction and cleanup flow.
	/// Verifies the cleanup system properly handles entity destruction.
	/// </summary>
	public class LuaDestroyCallbackTest
	{
		World m_World;
		EntityManager m_EntityManager;
		LuaScriptCleanupSystem m_CleanupSystem;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			m_World = World.DefaultGameObjectInjectionWorld;
			m_EntityManager = m_World.EntityManager;

			// Initialize registry for each test
			if (!LuaEntityRegistry.IsCreated)
				LuaEntityRegistry.Initialize();
			else
				LuaEntityRegistry.Clear();

			LuaTestUtilities.GetOrCreateTestVM();
			m_CleanupSystem = m_World.GetOrCreateSystemManaged<LuaScriptCleanupSystem>();
			yield return null;
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

			LuaEntityRegistry.Clear();
			yield return null;
		}

		[UnityTest]
		public IEnumerator DestroyRemovesLuaEntityIdFirst()
		{
			var entity = CreateScriptedEntityWithId("fruit");
			var entityId = m_EntityManager.GetComponentData<LuaEntityId>(entity).value;

			yield return null;
			yield return null;

			Assert.IsTrue(
				m_EntityManager.HasComponent<LuaEntityId>(entity),
				"Should have LuaEntityId before destroy"
			);

			using var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.TempJob);
			LuaEntityRegistry.Destroy(entityId, ecb);
			ecb.Playback(m_EntityManager);

			Assert.IsFalse(
				m_EntityManager.HasComponent<LuaEntityId>(entity),
				"LuaEntityId should be removed after destroy command"
			);
			Assert.IsTrue(
				m_EntityManager.HasComponent<LuaScript>(entity),
				"Should still have script buffer for cleanup"
			);
		}

		[UnityTest]
		public IEnumerator CleanupSystemDestroysEntityAfterCallbacks()
		{
			var entity = CreateScriptedEntityWithId("fruit");
			var entityId = m_EntityManager.GetComponentData<LuaEntityId>(entity).value;

			yield return null;
			yield return null;

			using var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.TempJob);
			LuaEntityRegistry.Destroy(entityId, ecb);
			ecb.Playback(m_EntityManager);

			Assert.IsTrue(m_EntityManager.Exists(entity), "Entity should exist after ID removal");

			// Check immediately after ID removal, before next frame when cleanup runs
			using var cleanupQuery = m_EntityManager.CreateEntityQuery(
				ComponentType.Exclude<LuaEntityId>(),
				ComponentType.ReadOnly<LuaScript>()
			);

			var orphanedEntities = cleanupQuery.ToEntityArray(Unity.Collections.Allocator.Persistent);
			var found = false;
			for (var i = 0; i < orphanedEntities.Length; i++)
			{
				if (orphanedEntities[i] == entity)
				{
					found = true;
					break;
				}
			}
			orphanedEntities.Dispose();
			Assert.IsTrue(found, "Entity should be visible to cleanup query after ID removal");

			// Now let cleanup system process and destroy the entity
			var framesWaited = 0;
			while (m_EntityManager.Exists(entity) && framesWaited < 10)
			{
				yield return null;
				framesWaited++;
			}

			Assert.IsFalse(m_EntityManager.Exists(entity), "Entity should be destroyed after cleanup");
		}

		[UnityTest]
		public IEnumerator OrphanedEntityDetectedByCleanupQuery()
		{
			var entity = CreateScriptedEntityWithId("fruit");

			yield return null;
			yield return null;

			using var query = m_EntityManager.CreateEntityQuery(
				ComponentType.Exclude<LuaEntityId>(),
				ComponentType.ReadOnly<LuaScript>()
			);

			var beforeCount = query.CalculateEntityCount();

			m_EntityManager.RemoveComponent<LuaEntityId>(entity);

			// Check immediately after ID removal, before cleanup system runs next frame
			var afterCount = query.CalculateEntityCount();
			Assert.AreEqual(
				beforeCount + 1,
				afterCount,
				"Should detect newly orphaned entity via cleanup query"
			);
		}

		Entity CreateScriptedEntityWithId(string scriptName)
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
			m_EntityManager.SetComponentData(entity, LocalTransform.FromPosition(0, 0, 0));

			var id = LuaEntityRegistry.Count + 1;
			LuaEntityRegistry.RegisterImmediate(entity, id, m_EntityManager);

			return entity;
		}
	}
}
