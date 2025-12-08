namespace LuaECS.Tests
{
	using System.Collections;
	using Components;
	using Core;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;
	using UnityEngine.TestTools;

	/// <summary>
	/// Comprehensive edge case tests for LuaEntityRegistry.
	/// Tests bidirectional lookup, pending state management, version safety, and cache consistency.
	/// </summary>
	public class LuaEntityRegistryTest
	{
		World m_World;
		EntityManager m_EntityManager;
		EntityCommandBufferSystem m_ECBSystem;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			m_World = World.DefaultGameObjectInjectionWorld;
			m_EntityManager = m_World.EntityManager;
			m_ECBSystem = m_World.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();

			// Initialize registry for each test (Clear resets NextId to 1)
			if (!LuaEntityRegistry.IsCreated)
				LuaEntityRegistry.Initialize(16);
			else
				LuaEntityRegistry.Clear();

			yield return null;
		}

		/// <summary>
		/// Creates a fresh ECB. Must be called at start of each test and after each yield.
		/// </summary>
		EntityCommandBuffer CreateECB() => m_ECBSystem.CreateCommandBuffer();

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			LuaEntityRegistry.Clear();

			var query = m_EntityManager.CreateEntityQuery(typeof(LuaEntityId));
			m_EntityManager.DestroyEntity(query);

			yield return null;
		}

		#region Create / Pending State

		[UnityTest]
		public IEnumerator Create_ReturnsPositiveId()
		{
			var ecb = CreateECB();
			var id = LuaEntityRegistry.Create(float3.zero, ecb);

			Assert.Greater(id, 0, "Created entity should have positive ID");
			yield return null;
		}

		[UnityTest]
		public IEnumerator Create_IdsAreSequential()
		{
			var ecb = CreateECB();
			var id1 = LuaEntityRegistry.Create(float3.zero, ecb);
			var id2 = LuaEntityRegistry.Create(float3.zero, ecb);
			var id3 = LuaEntityRegistry.Create(float3.zero, ecb);

			Assert.AreEqual(id1 + 1, id2, "IDs should be sequential");
			Assert.AreEqual(id2 + 1, id3, "IDs should be sequential");
			yield return null;
		}

		[UnityTest]
		public IEnumerator Create_EntityIsPendingBeforeCommit()
		{
			var ecb = CreateECB();
			var id = LuaEntityRegistry.Create(float3.zero, ecb);

			Assert.IsTrue(LuaEntityRegistry.IsPending(id), "Entity should be pending before commit");
			Assert.IsTrue(LuaEntityRegistry.Contains(id), "Contains should return true for pending");
			Assert.AreEqual(0, LuaEntityRegistry.Count, "Count should not include pending");

			yield return null;
		}

		[UnityTest]
		public IEnumerator Create_EntityResolvedAfterCommit()
		{
			var ecb = CreateECB();
			var id = LuaEntityRegistry.Create(float3.zero, ecb);

			yield return null; // ECB playback

			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			Assert.IsFalse(LuaEntityRegistry.IsPending(id), "Entity should not be pending after commit");
			Assert.IsTrue(LuaEntityRegistry.Contains(id), "Contains should still return true");
			Assert.AreEqual(1, LuaEntityRegistry.Count, "Count should include committed entity");

			var entity = LuaEntityRegistry.GetEntityFromId(id);
			Assert.AreNotEqual(Entity.Null, entity, "Should resolve to real entity");
			Assert.IsTrue(m_EntityManager.Exists(entity), "Entity should exist in world");
		}

		[UnityTest]
		public IEnumerator Create_MultipleEntitiesInSingleFrame()
		{
			var ecb = CreateECB();
			var ids = new int[10];
			for (var i = 0; i < 10; i++)
				ids[i] = LuaEntityRegistry.Create(new float3(i, 0, 0), ecb);

			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			Assert.AreEqual(10, LuaEntityRegistry.Count, "All entities should be committed");

			for (var i = 0; i < 10; i++)
			{
				var entity = LuaEntityRegistry.GetEntityFromId(ids[i]);
				Assert.AreNotEqual(Entity.Null, entity, $"Entity {i} should exist");
				var pos = m_EntityManager.GetComponentData<LocalTransform>(entity).Position;
				Assert.AreEqual(i, pos.x, 0.001f, $"Entity {i} should have correct position");
			}
		}

		#endregion

		#region Destroy / Pending Destruction

		[UnityTest]
		public IEnumerator Destroy_CommittedEntity()
		{
			var ecb = CreateECB();
			var id = LuaEntityRegistry.Create(float3.zero, ecb);
			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			var entity = LuaEntityRegistry.GetEntityFromId(id);
			Assert.IsTrue(m_EntityManager.Exists(entity), "Entity should exist before destroy");

			ecb = CreateECB();
			var destroyed = LuaEntityRegistry.Destroy(id, ecb);

			Assert.IsTrue(destroyed, "Destroy should return true");
			Assert.IsTrue(
				LuaEntityRegistry.IsMarkedForDestruction(id),
				"Should be marked for destruction"
			);
			Assert.IsTrue(LuaEntityRegistry.Contains(id), "Should still be in registry before commit");

			yield return null;
			LuaEntityRegistry.CommitPendingDestructions();

			Assert.IsFalse(LuaEntityRegistry.Contains(id), "Should be removed after commit");
			Assert.AreEqual(
				Entity.Null,
				LuaEntityRegistry.GetEntityFromId(id),
				"GetEntityFromId should return null"
			);
		}

		[UnityTest]
		public IEnumerator Destroy_PendingEntity_SameFrame()
		{
			var ecb = CreateECB();
			var id = LuaEntityRegistry.Create(float3.zero, ecb);

			Assert.IsTrue(LuaEntityRegistry.IsPending(id), "Should be pending");

			var destroyed = LuaEntityRegistry.Destroy(id, ecb);

			Assert.IsTrue(destroyed, "Destroy should succeed for pending entity");
			Assert.IsFalse(LuaEntityRegistry.IsPending(id), "Should no longer be pending");
			Assert.IsFalse(LuaEntityRegistry.Contains(id), "Should not be in registry");

			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);
			LuaEntityRegistry.CommitPendingDestructions();

			Assert.AreEqual(0, LuaEntityRegistry.Count, "Registry should be empty");
		}

		[UnityTest]
		public IEnumerator Destroy_NonExistentEntity_ReturnsFalse()
		{
			var ecb = CreateECB();
			var destroyed = LuaEntityRegistry.Destroy(999, ecb);

			Assert.IsFalse(destroyed, "Destroy should return false for non-existent entity");
			yield return null;
		}

		[UnityTest]
		public IEnumerator Destroy_MultipleEntitiesInSingleFrame()
		{
			var ecb = CreateECB();
			var ids = new int[5];
			for (var i = 0; i < 5; i++)
				ids[i] = LuaEntityRegistry.Create(float3.zero, ecb);

			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			Assert.AreEqual(5, LuaEntityRegistry.Count);

			ecb = CreateECB();
			foreach (var id in ids)
				LuaEntityRegistry.Destroy(id, ecb);

			yield return null;
			LuaEntityRegistry.CommitPendingDestructions();

			Assert.AreEqual(0, LuaEntityRegistry.Count, "All entities should be destroyed");
		}

		#endregion

		#region Register / RegisterImmediate

		[UnityTest]
		public IEnumerator Register_ExistingEntity_AssignsId()
		{
			var ecb = CreateECB();
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));

			var id = LuaEntityRegistry.Register(entity, ecb);

			Assert.Greater(id, 0, "Should assign positive ID");
			Assert.IsTrue(LuaEntityRegistry.Contains(id), "Should be in registry");
			Assert.AreEqual(
				entity,
				LuaEntityRegistry.GetEntityFromId(id),
				"Should resolve to same entity"
			);

			yield return null;
		}

		[UnityTest]
		public IEnumerator Register_SameEntityTwice_ReturnsSameId()
		{
			var ecb = CreateECB();
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));

			var id1 = LuaEntityRegistry.Register(entity, ecb);
			var id2 = LuaEntityRegistry.Register(entity, ecb);

			Assert.AreEqual(id1, id2, "Should return same ID for same entity");
			Assert.AreEqual(1, LuaEntityRegistry.Count, "Should only have one entry");

			yield return null;
		}

		[UnityTest]
		public IEnumerator RegisterImmediate_UpdatesNextIdCounter()
		{
			var ecb = CreateECB();
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
			m_EntityManager.AddComponentData(entity, new LuaEntityId { value = 100 });

			LuaEntityRegistry.RegisterImmediate(entity, 100, m_EntityManager);

			var newId = LuaEntityRegistry.Create(float3.zero, ecb);

			Assert.AreEqual(101, newId, "Next ID should be 101 after registering ID 100");

			yield return null;
		}

		[UnityTest]
		public IEnumerator RegisterImmediate_LowerId_DoesNotDecrementCounter()
		{
			var ecb = CreateECB();
			// First create some entities to advance counter
			LuaEntityRegistry.Create(float3.zero, ecb);
			LuaEntityRegistry.Create(float3.zero, ecb);
			LuaEntityRegistry.Create(float3.zero, ecb);

			// Register with lower ID
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
			LuaEntityRegistry.RegisterImmediate(entity, 1, m_EntityManager);

			// Next ID should still be 4, not 2
			var nextId = LuaEntityRegistry.Create(float3.zero, ecb);
			Assert.AreEqual(4, nextId, "Counter should not decrement");

			yield return null;
		}

		#endregion

		#region GetEntity / GetId Lookups

		[UnityTest]
		public IEnumerator GetEntityFromId_InvalidIds_ReturnsNull()
		{
			Assert.AreEqual(Entity.Null, LuaEntityRegistry.GetEntityFromId(0), "ID 0 should return null");
			Assert.AreEqual(
				Entity.Null,
				LuaEntityRegistry.GetEntityFromId(-1),
				"Negative ID should return null"
			);
			Assert.AreEqual(
				Entity.Null,
				LuaEntityRegistry.GetEntityFromId(-999),
				"Large negative should return null"
			);
			Assert.AreEqual(
				Entity.Null,
				LuaEntityRegistry.GetEntityFromId(int.MinValue),
				"MinValue should return null"
			);

			yield return null;
		}

		[UnityTest]
		public IEnumerator GetEntityFromId_NonExistentId_ReturnsNull()
		{
			Assert.AreEqual(
				Entity.Null,
				LuaEntityRegistry.GetEntityFromId(999),
				"Non-existent ID should return null"
			);
			yield return null;
		}

		[UnityTest]
		public IEnumerator GetIdFromEntity_NullEntity_ReturnsNegativeOne()
		{
			Assert.AreEqual(
				-1,
				LuaEntityRegistry.GetIdFromEntity(Entity.Null),
				"Null entity should return -1"
			);
			yield return null;
		}

		[UnityTest]
		public IEnumerator GetIdFromEntity_UnregisteredEntity_ReturnsNegativeOne()
		{
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
			Assert.AreEqual(
				-1,
				LuaEntityRegistry.GetIdFromEntity(entity),
				"Unregistered entity should return -1"
			);
			yield return null;
		}

		[UnityTest]
		public IEnumerator BidirectionalLookup_Consistent()
		{
			var ecb = CreateECB();
			var id = LuaEntityRegistry.Create(float3.zero, ecb);
			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			var entity = LuaEntityRegistry.GetEntityFromId(id);
			var lookupId = LuaEntityRegistry.GetIdFromEntity(entity);

			Assert.AreEqual(id, lookupId, "ID -> Entity -> ID should be consistent");
		}

		#endregion

		#region Entity Version Safety

		[UnityTest]
		public IEnumerator SyncWithWorld_DetectsDestroyedEntity_AutoCleanup()
		{
			var ecb = CreateECB();
			var id = LuaEntityRegistry.Create(float3.zero, ecb);
			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			var entity = LuaEntityRegistry.GetEntityFromId(id);
			Assert.AreNotEqual(Entity.Null, entity);

			// Destroy externally (not through registry)
			m_EntityManager.DestroyEntity(entity);

			// SyncWithWorld should detect this and auto-cleanup
			LuaEntityRegistry.SyncWithWorld(m_EntityManager);

			Assert.AreEqual(
				Entity.Null,
				LuaEntityRegistry.GetEntityFromId(id),
				"Should return null for destroyed entity"
			);
			Assert.IsFalse(LuaEntityRegistry.Contains(id), "Should auto-remove from registry");
		}

		[UnityTest]
		public IEnumerator SyncWithWorld_RemovesExternallyDestroyedEntities()
		{
			var ecb = CreateECB();
			var id1 = LuaEntityRegistry.Create(float3.zero, ecb);
			var id2 = LuaEntityRegistry.Create(float3.zero, ecb);
			var id3 = LuaEntityRegistry.Create(float3.zero, ecb);

			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			Assert.AreEqual(3, LuaEntityRegistry.Count);

			// Destroy entity2 externally
			var entity2 = LuaEntityRegistry.GetEntityFromId(id2);
			m_EntityManager.DestroyEntity(entity2);

			LuaEntityRegistry.SyncWithWorld(m_EntityManager);

			Assert.AreEqual(2, LuaEntityRegistry.Count, "Should have 2 entities after sync");
			Assert.IsTrue(LuaEntityRegistry.Contains(id1), "Entity 1 should remain");
			Assert.IsFalse(LuaEntityRegistry.Contains(id2), "Entity 2 should be removed");
			Assert.IsTrue(LuaEntityRegistry.Contains(id3), "Entity 3 should remain");
		}

		#endregion

		#region Commit Order and Edge Cases

		[UnityTest]
		public IEnumerator CommitCreations_CalledTwice_NoDuplicates()
		{
			var ecb = CreateECB();
			LuaEntityRegistry.Create(float3.zero, ecb);
			yield return null;

			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);
			var countAfterFirst = LuaEntityRegistry.Count;

			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);
			var countAfterSecond = LuaEntityRegistry.Count;

			Assert.AreEqual(countAfterFirst, countAfterSecond, "Double commit should not duplicate");
		}

		[UnityTest]
		public IEnumerator CommitDestructions_CalledTwice_Safe()
		{
			var ecb = CreateECB();
			var id = LuaEntityRegistry.Create(float3.zero, ecb);
			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			ecb = CreateECB();
			LuaEntityRegistry.Destroy(id, ecb);
			yield return null;

			LuaEntityRegistry.CommitPendingDestructions();
			LuaEntityRegistry.CommitPendingDestructions();

			Assert.AreEqual(0, LuaEntityRegistry.Count, "Should be empty after destroy");
		}

		[UnityTest]
		public IEnumerator CommitCreations_EmptyPending_NoOp()
		{
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);
			Assert.AreEqual(0, LuaEntityRegistry.Count);
			yield return null;
		}

		[UnityTest]
		public IEnumerator CommitDestructions_EmptyPending_NoOp()
		{
			LuaEntityRegistry.CommitPendingDestructions();
			Assert.AreEqual(0, LuaEntityRegistry.Count);
			yield return null;
		}

		#endregion

		#region GetAll / Enumeration

		[UnityTest]
		public IEnumerator GetAllIds_ReturnsOnlyCommitted()
		{
			var ecb = CreateECB();
			LuaEntityRegistry.Create(float3.zero, ecb);
			LuaEntityRegistry.Create(float3.zero, ecb);

			// Before commit, GetAllIds should be empty
			var idsBefore = LuaEntityRegistry.GetAllIds(Allocator.Persistent);
			Assert.AreEqual(0, idsBefore.Length, "Should be empty before commit");
			idsBefore.Dispose();

			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			var idsAfter = LuaEntityRegistry.GetAllIds(Allocator.Persistent);
			Assert.AreEqual(2, idsAfter.Length, "Should have 2 IDs after commit");
			idsAfter.Dispose();
		}

		[UnityTest]
		public IEnumerator GetAllEntities_ReturnsOnlyCommitted()
		{
			var ecb = CreateECB();
			LuaEntityRegistry.Create(float3.zero, ecb);
			LuaEntityRegistry.Create(float3.zero, ecb);

			var entitiesBefore = LuaEntityRegistry.GetAllEntities(Allocator.Persistent);
			Assert.AreEqual(0, entitiesBefore.Length, "Should be empty before commit");
			entitiesBefore.Dispose();

			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			var entitiesAfter = LuaEntityRegistry.GetAllEntities(Allocator.Persistent);
			Assert.AreEqual(2, entitiesAfter.Length, "Should have 2 entities after commit");
			entitiesAfter.Dispose();
		}

		#endregion

		#region Capacity / Stress

		[UnityTest]
		public IEnumerator Create_ExceedsInitialCapacity_Grows()
		{
			var ecb = CreateECB();
			// Initial capacity is 16
			for (var i = 0; i < 50; i++)
				LuaEntityRegistry.Create(float3.zero, ecb);

			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			Assert.AreEqual(
				50,
				LuaEntityRegistry.Count,
				"Should handle 50 entities despite 16 initial capacity"
			);
		}

		#endregion

		#region Clear / Lifecycle

		[UnityTest]
		public IEnumerator Clear_ResetsState()
		{
			var ecb = CreateECB();
			LuaEntityRegistry.Create(float3.zero, ecb);
			LuaEntityRegistry.Create(float3.zero, ecb);
			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			Assert.AreEqual(2, LuaEntityRegistry.Count);

			LuaEntityRegistry.Clear();

			Assert.AreEqual(0, LuaEntityRegistry.Count, "Count should be 0 after clear");
			Assert.IsTrue(LuaEntityRegistry.IsCreated, "Should still be created after clear");

			// New IDs should start from 1 - need fresh ECB after yield
			ecb = CreateECB();
			var newId = LuaEntityRegistry.Create(float3.zero, ecb);
			Assert.AreEqual(1, newId, "IDs should restart from 1 after clear");

			yield return null;
		}

		#endregion

		#region Complex Scenarios

		[UnityTest]
		public IEnumerator CreateDestroyCreate_SameFrame_IdsRemainUnique()
		{
			var ecb = CreateECB();
			var id1 = LuaEntityRegistry.Create(float3.zero, ecb);
			LuaEntityRegistry.Destroy(id1, ecb);
			var id2 = LuaEntityRegistry.Create(float3.zero, ecb);
			LuaEntityRegistry.Destroy(id2, ecb);
			var id3 = LuaEntityRegistry.Create(float3.zero, ecb);

			Assert.AreNotEqual(id1, id2, "IDs should be unique");
			Assert.AreNotEqual(id2, id3, "IDs should be unique");
			Assert.AreNotEqual(id1, id3, "IDs should be unique");

			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);
			LuaEntityRegistry.CommitPendingDestructions();

			Assert.AreEqual(1, LuaEntityRegistry.Count, "Only id3 should remain");
			Assert.IsTrue(LuaEntityRegistry.Contains(id3), "id3 should be in registry");
		}

		[UnityTest]
		public IEnumerator MixedRegisterAndCreate_IdsRemainUnique()
		{
			var ecb = CreateECB();
			var existing1 = m_EntityManager.CreateEntity(typeof(LocalTransform));
			var existing2 = m_EntityManager.CreateEntity(typeof(LocalTransform));

			var regId1 = LuaEntityRegistry.Register(existing1, ecb);
			var createId1 = LuaEntityRegistry.Create(float3.zero, ecb);
			var regId2 = LuaEntityRegistry.Register(existing2, ecb);
			var createId2 = LuaEntityRegistry.Create(float3.zero, ecb);

			var allIds = new[] { regId1, createId1, regId2, createId2 };
			for (var i = 0; i < allIds.Length; i++)
			{
				for (var j = i + 1; j < allIds.Length; j++)
				{
					Assert.AreNotEqual(allIds[i], allIds[j], $"IDs {i} and {j} should be unique");
				}
			}

			yield return null;
		}

		[UnityTest]
		public IEnumerator PendingEntity_VisibleViaContains_NotViaCount()
		{
			var ecb = CreateECB();
			var id = LuaEntityRegistry.Create(float3.zero, ecb);

			Assert.IsTrue(LuaEntityRegistry.Contains(id), "Contains should see pending");
			Assert.AreEqual(0, LuaEntityRegistry.Count, "Count should not see pending");
			Assert.IsTrue(LuaEntityRegistry.IsPending(id), "IsPending should return true");

			yield return null;
			LuaEntityRegistry.CommitPendingCreations(m_EntityManager);

			Assert.IsTrue(LuaEntityRegistry.Contains(id), "Contains should still see after commit");
			Assert.AreEqual(1, LuaEntityRegistry.Count, "Count should now include it");
			Assert.IsFalse(LuaEntityRegistry.IsPending(id), "IsPending should return false");
		}

		#endregion
	}
}
