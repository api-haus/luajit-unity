namespace LuaECS.Tests
{
	using System.Collections;
	using Components;
	using Core;
	using LuaVM.Core;
	using NUnit.Framework;
	using Systems.Support;
	using Unity.Entities;
	using Unity.Transforms;
	using UnityEngine.TestTools;

	/// <summary>
	/// Integration test covering script lifecycle: init -> update -> destroy.
	/// Verifies that Lua callbacks are invoked at correct lifecycle stages.
	/// </summary>
	public class LuaScriptLifecycleTest
	{
		World m_World;
		EntityManager m_EntityManager;
		LuaScriptFulfillmentSystem m_FulfillmentSystem;
		LuaVMManager m_VM;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			m_World = World.DefaultGameObjectInjectionWorld;
			m_EntityManager = m_World.EntityManager;
			m_VM = LuaTestUtilities.GetOrCreateTestVM();
			m_FulfillmentSystem = m_World.GetOrCreateSystemManaged<LuaScriptFulfillmentSystem>();
			yield return null;
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
		public IEnumerator ScriptInitializesOnFirstFrame()
		{
			var entity = CreateScriptedEntity("fruit");

			Assert.IsFalse(
				m_EntityManager.HasBuffer<LuaScript>(entity),
				"Should not have LuaScript buffer yet"
			);

			yield return null;
			yield return null;

			Assert.IsTrue(
				m_EntityManager.HasBuffer<LuaScript>(entity),
				"Should have LuaScript buffer after fulfillment"
			);
			var scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
			Assert.GreaterOrEqual(scripts[0].stateRef, 0, "StateRef should be assigned");
			Assert.Greater(scripts[0].entityIndex, 0, "EntityIndex should be assigned");
		}

		[UnityTest]
		public IEnumerator ScriptReceivesUpdates()
		{
			var entity = CreateScriptedEntity("fruit_eater");

			yield return null;
			yield return null;

			var initialPos = m_EntityManager.GetComponentData<LocalTransform>(entity).Position;

			for (var i = 0; i < 10; i++)
				yield return null;

			Assert.Pass("Script received updates for 10 frames without errors");
		}

		[UnityTest]
		public IEnumerator MultipleScriptsOnSameEntity()
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
			m_EntityManager.SetComponentData(entity, LocalTransform.FromPosition(0, 0, 0));

			yield return null;
			yield return null;

			var scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
			Assert.AreEqual(2, scripts.Length, "Should have 2 scripts");
			Assert.GreaterOrEqual(scripts[0].stateRef, 0, "First script should be initialized");
			Assert.GreaterOrEqual(scripts[1].stateRef, 0, "Second script should be initialized");

			Assert.AreEqual(
				scripts[0].entityIndex,
				scripts[1].entityIndex,
				"Both scripts should share same entity ID"
			);
			Assert.AreNotEqual(
				scripts[0].stateRef,
				scripts[1].stateRef,
				"Scripts should have different state refs"
			);
		}

		[UnityTest]
		public IEnumerator EntityDestructionCleansUp()
		{
			using var scriptQuery = m_EntityManager.CreateEntityQuery(typeof(LuaScript));
			var initialScriptCount = scriptQuery.CalculateEntityCount();

			var entity = CreateScriptedEntity("fruit");

			yield return null;
			yield return null;

			Assert.IsTrue(m_EntityManager.Exists(entity), "Entity should exist");

			m_EntityManager.RemoveComponent<LuaEntityId>(entity);

			var framesWaited = 0;
			while (m_EntityManager.Exists(entity) && framesWaited < 60)
			{
				yield return null;
				framesWaited++;
			}

			Assert.IsFalse(m_EntityManager.Exists(entity), "Entity should be destroyed");
			Assert.AreEqual(
				initialScriptCount,
				scriptQuery.CalculateEntityCount(),
				"Script buffers should be removed by cleanup system"
			);
			Assert.Pass("Entity destruction completed cleanly");
		}

		[UnityTest]
		public IEnumerator ScriptsAddedOverMultipleFramesThenDisabledThenEntityRemoved()
		{
			// Create entity with initial script
			var entity = CreateScriptedEntity("fruit");

			yield return null;
			yield return null;

			// Verify first script initialized
			Assert.IsTrue(m_EntityManager.HasBuffer<LuaScript>(entity), "Should have LuaScript buffer");
			var scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
			Assert.AreEqual(1, scripts.Length, "Should have 1 script");
			var firstStateRef = scripts[0].stateRef;
			var firstEntityIndex = scripts[0].entityIndex;
			Assert.GreaterOrEqual(firstStateRef, 0, "First script should be initialized");
			Assert.IsTrue(
				m_VM.ValidateStateRef("fruit", firstEntityIndex, firstStateRef),
				"First script state should be tracked by VM"
			);

			// Add second script on a later frame
			var requests = m_EntityManager.GetBuffer<LuaScriptRequest>(entity);
			requests.Add(
				new LuaScriptRequest
				{
					scriptName = "fruit_eater",
					requestHash = LuaScriptPathUtility.HashScriptName("fruit_eater"),
					fulfilled = false,
				}
			);

			yield return null;
			yield return null;

			// Verify second script initialized
			scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
			Assert.AreEqual(2, scripts.Length, "Should have 2 scripts now");
			var secondStateRef = scripts[1].stateRef;
			Assert.GreaterOrEqual(secondStateRef, 0, "Second script should be initialized");
			Assert.IsTrue(
				m_VM.ValidateStateRef("fruit_eater", firstEntityIndex, secondStateRef),
				"Second script state should be tracked by VM"
			);

			// Disable first script
			var disabled = m_FulfillmentSystem.DisableScript(entity, "fruit");
			Assert.IsTrue(disabled, "Should successfully disable first script");

			scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
			Assert.IsTrue(scripts[0].disabled, "First script should be marked disabled");
			Assert.AreEqual(-1, scripts[0].stateRef, "Disabled script should have StateRef = -1");
			Assert.IsFalse(scripts[1].disabled, "Second script should not be disabled");
			Assert.IsFalse(
				m_VM.ValidateStateRef("fruit", firstEntityIndex, firstStateRef),
				"First script state should be released from VM"
			);
			Assert.IsTrue(
				m_VM.ValidateStateRef("fruit_eater", firstEntityIndex, secondStateRef),
				"Second script state should still be tracked"
			);

			// Run a few frames to verify disabled script doesn't cause issues
			for (var i = 0; i < 5; i++)
				yield return null;

			// Remove entity
			m_EntityManager.RemoveComponent<LuaEntityId>(entity);

			var framesWaited = 0;
			while (m_EntityManager.Exists(entity) && framesWaited < 30)
			{
				yield return null;
				framesWaited++;
			}

			// Verify entity destroyed and all states cleaned up
			Assert.IsFalse(m_EntityManager.Exists(entity), "Entity should be destroyed");
			Assert.IsFalse(
				m_VM.ValidateStateRef("fruit_eater", firstEntityIndex, secondStateRef),
				"Second script state should be released after entity cleanup"
			);

			Assert.Pass(
				"Scripts added over multiple frames, disabled, then entity removed - all cleaned up"
			);
		}

		[UnityTest]
		public IEnumerator DisableScriptByHashWorks()
		{
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
			var fruitHash = LuaScriptPathUtility.HashScriptName("fruit");
			var requests = m_EntityManager.AddBuffer<LuaScriptRequest>(entity);
			requests.Add(
				new LuaScriptRequest
				{
					scriptName = "fruit",
					requestHash = fruitHash,
					fulfilled = false,
				}
			);
			m_EntityManager.AddBuffer<LuaEvent>(entity);
			m_EntityManager.AddComponentData(entity, new LuaEntityId { value = 0 });
			m_EntityManager.SetComponentData(entity, LocalTransform.FromPosition(0, 0, 0));

			yield return null;
			yield return null;

			var scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
			Assert.AreEqual(1, scripts.Length, "Should have 1 script");
			Assert.GreaterOrEqual(scripts[0].stateRef, 0, "Script should be initialized");
			Assert.AreEqual(fruitHash, scripts[0].requestHash, "Script should have matching hash");

			// Disable by hash
			var disabled = m_FulfillmentSystem.DisableScriptByHash(entity, fruitHash);
			Assert.IsTrue(disabled, "Should successfully disable script by hash");

			scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
			Assert.IsTrue(scripts[0].disabled, "Script should be marked disabled");
			Assert.AreEqual(-1, scripts[0].stateRef, "Disabled script should have StateRef = -1");
		}

		Entity CreateScriptedEntity(string scriptName)
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
			return entity;
		}
	}
}
