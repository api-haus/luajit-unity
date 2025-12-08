namespace LuaECS.Tests
{
	using System.Collections;
	using LuaECS.Components;
	using LuaECS.Core;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;
	using UnityEngine.TestTools;

	/// <summary>
	/// Comprehensive edge case tests for LuaEntityCollection.
	/// Tests bidirectional lookup, pending state management, version safety, and cache consistency.
	/// </summary>
	public class LuaEntityCollectionTest
	{
		World m_World;
		EntityManager m_EntityManager;
		LuaEntityCollection m_Collection;
		EntityCommandBufferSystem m_ECBSystem;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			m_World = World.DefaultGameObjectInjectionWorld;
			m_EntityManager = m_World.EntityManager;
			m_Collection = new LuaEntityCollection(m_EntityManager, 16);
			m_ECBSystem = m_World.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
			yield return null;
		}

		/// <summary>
		/// Creates a fresh ECB. Must be called at start of each test and after each yield.
		/// </summary>
		EntityCommandBuffer CreateECB() => m_ECBSystem.CreateCommandBuffer();

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			m_Collection?.Dispose();

			var query = m_EntityManager.CreateEntityQuery(typeof(LuaEntityId));
			m_EntityManager.DestroyEntity(query);

			yield return null;
		}

		#region Create / Pending State

		[UnityTest]
		public IEnumerator Create_ReturnsPositiveId()
		{
			var ecb = CreateECB();
			var id = m_Collection.Create(float3.zero, ecb);

			Assert.Greater(id, 0, "Created entity should have positive ID");
			yield return null;
		}

		[UnityTest]
		public IEnumerator Create_IdsAreSequential()
		{
			var ecb = CreateECB();
			var id1 = m_Collection.Create(float3.zero, ecb);
			var id2 = m_Collection.Create(float3.zero, ecb);
			var id3 = m_Collection.Create(float3.zero, ecb);

			Assert.AreEqual(id1 + 1, id2, "IDs should be sequential");
			Assert.AreEqual(id2 + 1, id3, "IDs should be sequential");
			yield return null;
		}

		[UnityTest]
		public IEnumerator Create_EntityIsPendingBeforeCommit()
		{
			var ecb = CreateECB();
			var id = m_Collection.Create(float3.zero, ecb);

			Assert.IsTrue(m_Collection.IsPending(id), "Entity should be pending before commit");
			Assert.IsTrue(m_Collection.Contains(id), "Contains should return true for pending");
			Assert.AreEqual(0, m_Collection.Count, "Count should not include pending");

			yield return null;
		}

		[UnityTest]
		public IEnumerator Create_EntityResolvedAfterCommit()
		{
			var ecb = CreateECB();
			var id = m_Collection.Create(float3.zero, ecb);

			yield return null; // ECB playback

			m_Collection.CommitPendingCreations();

			Assert.IsFalse(m_Collection.IsPending(id), "Entity should not be pending after commit");
			Assert.IsTrue(m_Collection.Contains(id), "Contains should still return true");
			Assert.AreEqual(1, m_Collection.Count, "Count should include committed entity");

			var entity = m_Collection.GetEntity(id);
			Assert.AreNotEqual(Entity.Null, entity, "Should resolve to real entity");
			Assert.IsTrue(m_EntityManager.Exists(entity), "Entity should exist in world");
		}

		[UnityTest]
		public IEnumerator Create_MultipleEntitiesInSingleFrame()
		{
			var ecb = CreateECB();
			var ids = new int[10];
			for (var i = 0; i < 10; i++)
				ids[i] = m_Collection.Create(new float3(i, 0, 0), ecb);

			yield return null;
			m_Collection.CommitPendingCreations();

			Assert.AreEqual(10, m_Collection.Count, "All entities should be committed");

			for (var i = 0; i < 10; i++)
			{
				var entity = m_Collection.GetEntity(ids[i]);
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
			var id = m_Collection.Create(float3.zero, ecb);
			yield return null;
			m_Collection.CommitPendingCreations();

			var entity = m_Collection.GetEntity(id);
			Assert.IsTrue(m_EntityManager.Exists(entity), "Entity should exist before destroy");

			ecb = CreateECB();
			var destroyed = m_Collection.Destroy(id, ecb);

			Assert.IsTrue(destroyed, "Destroy should return true");
			Assert.IsTrue(m_Collection.IsMarkedForDestruction(id), "Should be marked for destruction");
			Assert.IsTrue(m_Collection.Contains(id), "Should still be in collection before commit");

			yield return null;
			m_Collection.CommitPendingDestructions();

			Assert.IsFalse(m_Collection.Contains(id), "Should be removed after commit");
			Assert.AreEqual(Entity.Null, m_Collection.GetEntity(id), "GetEntity should return null");
		}

		[UnityTest]
		public IEnumerator Destroy_PendingEntity_SameFrame()
		{
			var ecb = CreateECB();
			var id = m_Collection.Create(float3.zero, ecb);

			Assert.IsTrue(m_Collection.IsPending(id), "Should be pending");

			var destroyed = m_Collection.Destroy(id, ecb);

			Assert.IsTrue(destroyed, "Destroy should succeed for pending entity");
			Assert.IsFalse(m_Collection.IsPending(id), "Should no longer be pending");
			Assert.IsFalse(m_Collection.Contains(id), "Should not be in collection");

			yield return null;
			m_Collection.CommitPendingCreations();
			m_Collection.CommitPendingDestructions();

			Assert.AreEqual(0, m_Collection.Count, "Collection should be empty");
		}

		[UnityTest]
		public IEnumerator Destroy_NonExistentEntity_ReturnsFalse()
		{
			var ecb = CreateECB();
			var destroyed = m_Collection.Destroy(999, ecb);

			Assert.IsFalse(destroyed, "Destroy should return false for non-existent entity");
			yield return null;
		}

		[UnityTest]
		public IEnumerator Destroy_MultipleEntitiesInSingleFrame()
		{
			var ecb = CreateECB();
			var ids = new int[5];
			for (var i = 0; i < 5; i++)
				ids[i] = m_Collection.Create(float3.zero, ecb);

			yield return null;
			m_Collection.CommitPendingCreations();

			Assert.AreEqual(5, m_Collection.Count);

			ecb = CreateECB();
			foreach (var id in ids)
				m_Collection.Destroy(id, ecb);

			yield return null;
			m_Collection.CommitPendingDestructions();

			Assert.AreEqual(0, m_Collection.Count, "All entities should be destroyed");
		}

		#endregion

		#region Register / RegisterImmediate

		[UnityTest]
		public IEnumerator Register_ExistingEntity_AssignsId()
		{
			var ecb = CreateECB();
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));

			var id = m_Collection.Register(entity, ecb);

			Assert.Greater(id, 0, "Should assign positive ID");
			Assert.IsTrue(m_Collection.Contains(id), "Should be in collection");
			Assert.AreEqual(entity, m_Collection.GetEntity(id), "Should resolve to same entity");

			yield return null;
		}

		[UnityTest]
		public IEnumerator Register_SameEntityTwice_ReturnsSameId()
		{
			var ecb = CreateECB();
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));

			var id1 = m_Collection.Register(entity, ecb);
			var id2 = m_Collection.Register(entity, ecb);

			Assert.AreEqual(id1, id2, "Should return same ID for same entity");
			Assert.AreEqual(1, m_Collection.Count, "Should only have one entry");

			yield return null;
		}

		[UnityTest]
		public IEnumerator RegisterImmediate_UpdatesNextIdCounter()
		{
			var ecb = CreateECB();
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
			m_EntityManager.AddComponentData(entity, new LuaEntityId { Value = 100 });

			m_Collection.RegisterImmediate(entity, 100);

			var newId = m_Collection.Create(float3.zero, ecb);

			Assert.AreEqual(101, newId, "Next ID should be 101 after registering ID 100");

			yield return null;
		}

		[UnityTest]
		public IEnumerator RegisterImmediate_LowerId_DoesNotDecrementCounter()
		{
			var ecb = CreateECB();
			// First create some entities to advance counter
			m_Collection.Create(float3.zero, ecb);
			m_Collection.Create(float3.zero, ecb);
			m_Collection.Create(float3.zero, ecb);

			// Register with lower ID
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
			m_Collection.RegisterImmediate(entity, 1);

			// Next ID should still be 4, not 2
			var nextId = m_Collection.Create(float3.zero, ecb);
			Assert.AreEqual(4, nextId, "Counter should not decrement");

			yield return null;
		}

		#endregion

		#region GetEntity / GetId Lookups

		[UnityTest]
		public IEnumerator GetEntity_InvalidIds_ReturnsNull()
		{
			Assert.AreEqual(Entity.Null, m_Collection.GetEntity(0), "ID 0 should return null");
			Assert.AreEqual(Entity.Null, m_Collection.GetEntity(-1), "Negative ID should return null");
			Assert.AreEqual(
				Entity.Null,
				m_Collection.GetEntity(-999),
				"Large negative should return null"
			);
			Assert.AreEqual(
				Entity.Null,
				m_Collection.GetEntity(int.MinValue),
				"MinValue should return null"
			);

			yield return null;
		}

		[UnityTest]
		public IEnumerator GetEntity_NonExistentId_ReturnsNull()
		{
			Assert.AreEqual(
				Entity.Null,
				m_Collection.GetEntity(999),
				"Non-existent ID should return null"
			);
			yield return null;
		}

		[UnityTest]
		public IEnumerator GetId_NullEntity_ReturnsNegativeOne()
		{
			Assert.AreEqual(-1, m_Collection.GetId(Entity.Null), "Null entity should return -1");
			yield return null;
		}

		[UnityTest]
		public IEnumerator GetId_UnregisteredEntity_ReturnsNegativeOne()
		{
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
			Assert.AreEqual(-1, m_Collection.GetId(entity), "Unregistered entity should return -1");
			yield return null;
		}

		[UnityTest]
		public IEnumerator BidirectionalLookup_Consistent()
		{
			var ecb = CreateECB();
			var id = m_Collection.Create(float3.zero, ecb);
			yield return null;
			m_Collection.CommitPendingCreations();

			var entity = m_Collection.GetEntity(id);
			var lookupId = m_Collection.GetId(entity);

			Assert.AreEqual(id, lookupId, "ID → Entity → ID should be consistent");
		}

		#endregion

		#region Entity Version Safety

		[UnityTest]
		public IEnumerator GetEntity_DetectsDestroyedEntity_AutoCleanup()
		{
			var ecb = CreateECB();
			var id = m_Collection.Create(float3.zero, ecb);
			yield return null;
			m_Collection.CommitPendingCreations();

			var entity = m_Collection.GetEntity(id);
			Assert.AreNotEqual(Entity.Null, entity);

			// Destroy externally (not through collection)
			m_EntityManager.DestroyEntity(entity);

			// GetEntity should detect this and auto-cleanup
			var result = m_Collection.GetEntity(id);

			Assert.AreEqual(Entity.Null, result, "Should return null for destroyed entity");
			Assert.IsFalse(m_Collection.Contains(id), "Should auto-remove from collection");
		}

		[UnityTest]
		public IEnumerator Contains_DetectsDestroyedEntity_AutoCleanup()
		{
			var ecb = CreateECB();
			var id = m_Collection.Create(float3.zero, ecb);
			yield return null;
			m_Collection.CommitPendingCreations();

			var entity = m_Collection.GetEntity(id);

			// Destroy externally
			m_EntityManager.DestroyEntity(entity);

			// Contains should detect this
			Assert.IsFalse(m_Collection.Contains(id), "Should return false for destroyed entity");
			Assert.AreEqual(0, m_Collection.Count, "Should be removed from count");
		}

		[UnityTest]
		public IEnumerator SyncWithWorld_RemovesExternallyDestroyedEntities()
		{
			var ecb = CreateECB();
			var id1 = m_Collection.Create(float3.zero, ecb);
			var id2 = m_Collection.Create(float3.zero, ecb);
			var id3 = m_Collection.Create(float3.zero, ecb);

			yield return null;
			m_Collection.CommitPendingCreations();

			Assert.AreEqual(3, m_Collection.Count);

			// Destroy entity2 externally
			var entity2 = m_Collection.GetEntity(id2);
			m_EntityManager.DestroyEntity(entity2);

			// Before sync, count is still 3 (unless we accessed id2)
			m_Collection.SyncWithWorld();

			Assert.AreEqual(2, m_Collection.Count, "Should have 2 entities after sync");
			Assert.IsTrue(m_Collection.Contains(id1), "Entity 1 should remain");
			Assert.IsFalse(m_Collection.Contains(id2), "Entity 2 should be removed");
			Assert.IsTrue(m_Collection.Contains(id3), "Entity 3 should remain");
		}

		#endregion

		#region Commit Order and Edge Cases

		[UnityTest]
		public IEnumerator CommitCreations_CalledTwice_NoDuplicates()
		{
			var ecb = CreateECB();
			m_Collection.Create(float3.zero, ecb);
			yield return null;

			m_Collection.CommitPendingCreations();
			var countAfterFirst = m_Collection.Count;

			m_Collection.CommitPendingCreations();
			var countAfterSecond = m_Collection.Count;

			Assert.AreEqual(countAfterFirst, countAfterSecond, "Double commit should not duplicate");
		}

		[UnityTest]
		public IEnumerator CommitDestructions_CalledTwice_Safe()
		{
			var ecb = CreateECB();
			var id = m_Collection.Create(float3.zero, ecb);
			yield return null;
			m_Collection.CommitPendingCreations();

			ecb = CreateECB();
			m_Collection.Destroy(id, ecb);
			yield return null;

			m_Collection.CommitPendingDestructions();
			m_Collection.CommitPendingDestructions();

			Assert.AreEqual(0, m_Collection.Count, "Should be empty after destroy");
		}

		[UnityTest]
		public IEnumerator CommitCreations_EmptyPending_NoOp()
		{
			m_Collection.CommitPendingCreations();
			Assert.AreEqual(0, m_Collection.Count);
			yield return null;
		}

		[UnityTest]
		public IEnumerator CommitDestructions_EmptyPending_NoOp()
		{
			m_Collection.CommitPendingDestructions();
			Assert.AreEqual(0, m_Collection.Count);
			yield return null;
		}

		#endregion

		#region GetAll / Enumeration

		[UnityTest]
		public IEnumerator GetAllIds_ReturnsOnlyCommitted()
		{
			var ecb = CreateECB();
			m_Collection.Create(float3.zero, ecb);
			m_Collection.Create(float3.zero, ecb);

			// Before commit, GetAllIds should be empty
			var idsBefore = m_Collection.GetAllIds(Allocator.Persistent);
			Assert.AreEqual(0, idsBefore.Length, "Should be empty before commit");
			idsBefore.Dispose();

			yield return null;
			m_Collection.CommitPendingCreations();

			var idsAfter = m_Collection.GetAllIds(Allocator.Persistent);
			Assert.AreEqual(2, idsAfter.Length, "Should have 2 IDs after commit");
			idsAfter.Dispose();
		}

		[UnityTest]
		public IEnumerator GetAllEntities_ReturnsOnlyCommitted()
		{
			var ecb = CreateECB();
			m_Collection.Create(float3.zero, ecb);
			m_Collection.Create(float3.zero, ecb);

			var entitiesBefore = m_Collection.GetAllEntities(Allocator.Persistent);
			Assert.AreEqual(0, entitiesBefore.Length, "Should be empty before commit");
			entitiesBefore.Dispose();

			yield return null;
			m_Collection.CommitPendingCreations();

			var entitiesAfter = m_Collection.GetAllEntities(Allocator.Persistent);
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
				m_Collection.Create(float3.zero, ecb);

			yield return null;
			m_Collection.CommitPendingCreations();

			Assert.AreEqual(
				50,
				m_Collection.Count,
				"Should handle 50 entities despite 16 initial capacity"
			);
		}

		#endregion

		#region Disposal

		[UnityTest]
		public IEnumerator Dispose_SetsIsCreatedFalse()
		{
			Assert.IsTrue(m_Collection.IsCreated, "Should be created initially");

			m_Collection.Dispose();

			Assert.IsFalse(m_Collection.IsCreated, "Should not be created after dispose");

			// Prevent double dispose in TearDown
			m_Collection = null;
			yield return null;
		}

		#endregion

		#region Complex Scenarios

		[UnityTest]
		public IEnumerator CreateDestroyCreate_SameFrame_IdsRemainUnique()
		{
			var ecb = CreateECB();
			var id1 = m_Collection.Create(float3.zero, ecb);
			m_Collection.Destroy(id1, ecb);
			var id2 = m_Collection.Create(float3.zero, ecb);
			m_Collection.Destroy(id2, ecb);
			var id3 = m_Collection.Create(float3.zero, ecb);

			Assert.AreNotEqual(id1, id2, "IDs should be unique");
			Assert.AreNotEqual(id2, id3, "IDs should be unique");
			Assert.AreNotEqual(id1, id3, "IDs should be unique");

			yield return null;
			m_Collection.CommitPendingCreations();
			m_Collection.CommitPendingDestructions();

			Assert.AreEqual(1, m_Collection.Count, "Only id3 should remain");
			Assert.IsTrue(m_Collection.Contains(id3), "id3 should be in collection");
		}

		[UnityTest]
		public IEnumerator MixedRegisterAndCreate_IdsRemainUnique()
		{
			var ecb = CreateECB();
			var existing1 = m_EntityManager.CreateEntity(typeof(LocalTransform));
			var existing2 = m_EntityManager.CreateEntity(typeof(LocalTransform));

			var regId1 = m_Collection.Register(existing1, ecb);
			var createId1 = m_Collection.Create(float3.zero, ecb);
			var regId2 = m_Collection.Register(existing2, ecb);
			var createId2 = m_Collection.Create(float3.zero, ecb);

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
			var id = m_Collection.Create(float3.zero, ecb);

			Assert.IsTrue(m_Collection.Contains(id), "Contains should see pending");
			Assert.AreEqual(0, m_Collection.Count, "Count should not see pending");
			Assert.IsTrue(m_Collection.IsPending(id), "IsPending should return true");

			yield return null;
			m_Collection.CommitPendingCreations();

			Assert.IsTrue(m_Collection.Contains(id), "Contains should still see after commit");
			Assert.AreEqual(1, m_Collection.Count, "Count should now include it");
			Assert.IsFalse(m_Collection.IsPending(id), "IsPending should return false");
		}

		#endregion
	}
}
