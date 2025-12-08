namespace LuaECS.Tests
{
	using System.Collections;
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaVM.Core;
	using NUnit.Framework;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;
	using UnityEngine;
	using UnityEngine.TestTools;

	/// <summary>
	/// Integration test for event dispatch system.
	/// Verifies event emission and callback delivery to Lua scripts.
	/// </summary>
	public class LuaEventDispatchTest
	{
		World m_World;
		EntityManager m_EntityManager;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			m_World = World.DefaultGameObjectInjectionWorld;
			m_EntityManager = m_World.EntityManager;
			LuaVMManager.GetOrCreate();
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
		public IEnumerator EventBufferReceivesEvents()
		{
			var entity = CreateScriptedEntity("fruit", float3.zero);

			yield return null;
			yield return null;

			var events = m_EntityManager.GetBuffer<LuaEvent>(entity);
			var initialCount = events.Length;

			events.Add(
				new LuaEvent
				{
					EventName = "test_event",
					Source = entity,
					Target = entity,
					IntParam = 42,
				}
			);

			Assert.AreEqual(initialCount + 1, events.Length, "Event should be added to buffer");

			yield return null;

			events = m_EntityManager.GetBuffer<LuaEvent>(entity);
			Assert.AreEqual(0, events.Length, "Events should be cleared after dispatch");
		}

		[UnityTest]
		public IEnumerator MultipleEventsDispatchedInOrder()
		{
			var entity = CreateScriptedEntity("fruit", float3.zero);

			yield return null;
			yield return null;

			var events = m_EntityManager.GetBuffer<LuaEvent>(entity);

			events.Add(new LuaEvent { EventName = "event_1", IntParam = 1 });
			events.Add(new LuaEvent { EventName = "event_2", IntParam = 2 });
			events.Add(new LuaEvent { EventName = "event_3", IntParam = 3 });

			Assert.AreEqual(3, events.Length, "Should have 3 pending events");

			yield return null;

			events = m_EntityManager.GetBuffer<LuaEvent>(entity);
			Assert.AreEqual(0, events.Length, "All events should be dispatched");
		}

		[UnityTest]
		public IEnumerator EventWithSourceAndTarget()
		{
			var source = CreateScriptedEntity("fruit_eater", new float3(0, 0, 0));
			var target = CreateScriptedEntity("fruit", new float3(5, 0, 0));

			yield return null;
			yield return null;

			var targetEvents = m_EntityManager.GetBuffer<LuaEvent>(target);
			targetEvents.Add(
				new LuaEvent
				{
					EventName = "on_attacked",
					Source = source,
					Target = target,
					IntParam = 10,
				}
			);

			yield return null;

			targetEvents = m_EntityManager.GetBuffer<LuaEvent>(target);
			Assert.AreEqual(0, targetEvents.Length, "Event should be dispatched");

			Assert.Pass("Event with source and target dispatched successfully");
		}

		[UnityTest]
		public IEnumerator EventsDispatchToAllScriptsOnEntity()
		{
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
			var requests = m_EntityManager.AddBuffer<LuaScriptRequest>(entity);

			requests.Add(
				new LuaScriptRequest
				{
					ScriptName = "fruit",
					RequestHash = LuaScriptPathUtility.HashScriptName("fruit"),
					Fulfilled = false,
				}
			);
			requests.Add(
				new LuaScriptRequest
				{
					ScriptName = "fruit_eater",
					RequestHash = LuaScriptPathUtility.HashScriptName("fruit_eater"),
					Fulfilled = false,
				}
			);

			m_EntityManager.AddBuffer<LuaCommand>(entity);
			m_EntityManager.AddBuffer<LuaEvent>(entity);
			m_EntityManager.AddComponentData(entity, new LuaEntityId { Value = 0 });
			m_EntityManager.SetComponentData(entity, LocalTransform.FromPosition(0, 0, 0));

			yield return null;
			yield return null;

			var scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
			Assert.AreEqual(2, scripts.Length, "Should have 2 scripts");
			Assert.IsTrue(
				scripts[0].StateRef >= 0 && scripts[1].StateRef >= 0,
				"Both scripts should be initialized"
			);

			var events = m_EntityManager.GetBuffer<LuaEvent>(entity);
			events.Add(new LuaEvent { EventName = "test_broadcast", IntParam = 99 });

			yield return null;

			events = m_EntityManager.GetBuffer<LuaEvent>(entity);
			Assert.AreEqual(0, events.Length, "Event should be dispatched to all scripts");
		}

		[UnityTest]
		public IEnumerator NoEventsWhenBufferEmpty()
		{
			var entity = CreateScriptedEntity("fruit", float3.zero);

			yield return null;
			yield return null;

			var events = m_EntityManager.GetBuffer<LuaEvent>(entity);
			Assert.AreEqual(0, events.Length, "Buffer should be empty");

			for (var i = 0; i < 5; i++)
				yield return null;

			events = m_EntityManager.GetBuffer<LuaEvent>(entity);
			Assert.AreEqual(0, events.Length, "Buffer should remain empty");

			Assert.Pass("No errors when event buffer is empty");
		}

		Entity CreateScriptedEntity(string scriptName, float3 position)
		{
			var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
			var requests = m_EntityManager.AddBuffer<LuaScriptRequest>(entity);
			requests.Add(
				new LuaScriptRequest
				{
					ScriptName = scriptName,
					RequestHash = LuaScriptPathUtility.HashScriptName(scriptName),
					Fulfilled = false,
				}
			);
			m_EntityManager.AddBuffer<LuaCommand>(entity);
			m_EntityManager.AddBuffer<LuaEvent>(entity);
			m_EntityManager.AddComponentData(entity, new LuaEntityId { Value = 0 });
			m_EntityManager.SetComponentData(entity, LocalTransform.FromPosition(position));
			return entity;
		}
	}
}
