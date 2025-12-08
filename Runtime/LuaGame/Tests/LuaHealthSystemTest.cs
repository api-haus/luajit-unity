namespace LuaGame.Tests
{
	using System.Collections;
	using LuaECS.Components;
	using LuaGame.Components;
	using LuaVM.Core;
	using NUnit.Framework;
	using Unity.Entities;
	using Unity.Transforms;
	using UnityEngine.TestTools;

	/// <summary>
	/// Integration tests for LuaHealthSystem.
	/// Verifies death detection and OnDeath event generation.
	/// </summary>
	public class LuaHealthSystemTest
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
			var query = m_EntityManager.CreateEntityQuery(typeof(LuaHealth));
			m_EntityManager.DestroyEntity(query);
			yield return null;
		}

		[UnityTest]
		public IEnumerator DeathTriggersOnDeathEvent()
		{
			var entity = CreateHealthEntity(100f);

			yield return null;

			var health = m_EntityManager.GetComponentData<LuaHealth>(entity);
			health.current = 0;
			m_EntityManager.SetComponentData(entity, health);

			yield return null;

			health = m_EntityManager.GetComponentData<LuaHealth>(entity);
			Assert.IsTrue(health.isDead, "Entity should be marked as dead");

			var events = m_EntityManager.GetBuffer<LuaEvent>(entity);
			Assert.AreEqual(1, events.Length, "Should have one OnDeath event");
			Assert.AreEqual("OnDeath", events[0].eventName.ToString());
		}

		[UnityTest]
		public IEnumerator DeathEventOnlyTriggersOnce()
		{
			var entity = CreateHealthEntity(100f);

			yield return null;

			var health = m_EntityManager.GetComponentData<LuaHealth>(entity);
			health.current = 0;
			m_EntityManager.SetComponentData(entity, health);

			yield return null;
			yield return null;
			yield return null;

			var events = m_EntityManager.GetBuffer<LuaEvent>(entity);
			Assert.AreEqual(1, events.Length, "OnDeath should only trigger once");
		}

		[UnityTest]
		public IEnumerator HealthyEntityDoesNotTriggerDeath()
		{
			var entity = CreateHealthEntity(100f);

			yield return null;
			yield return null;

			var health = m_EntityManager.GetComponentData<LuaHealth>(entity);
			Assert.IsFalse(health.isDead, "Entity should not be dead");

			var events = m_EntityManager.GetBuffer<LuaEvent>(entity);
			Assert.AreEqual(0, events.Length, "No death events for healthy entity");
		}

		[UnityTest]
		public IEnumerator DamagedButAliveDoesNotTriggerDeath()
		{
			var entity = CreateHealthEntity(100f);

			yield return null;

			var health = m_EntityManager.GetComponentData<LuaHealth>(entity);
			health.current = 1f;
			m_EntityManager.SetComponentData(entity, health);

			yield return null;

			health = m_EntityManager.GetComponentData<LuaHealth>(entity);
			Assert.IsFalse(health.isDead, "Entity should not be dead with 1 HP");

			var events = m_EntityManager.GetBuffer<LuaEvent>(entity);
			Assert.AreEqual(0, events.Length, "No death events for alive entity");
		}

		Entity CreateHealthEntity(float maxHealth)
		{
			var entity = m_EntityManager.CreateEntity(
				typeof(LocalTransform),
				typeof(LuaHealth),
				typeof(LuaEvent)
			);
			m_EntityManager.SetComponentData(entity, LuaHealth.Create(maxHealth));
			m_EntityManager.SetComponentData(entity, LocalTransform.FromPosition(0, 0, 0));
			return entity;
		}
	}
}
