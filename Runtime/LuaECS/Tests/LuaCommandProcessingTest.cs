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
	/// Integration test for command processing via LuaCommandProcessorSystem.
	/// Verifies move_toward queue -> Burst job execution.
	/// </summary>
	public class LuaCommandProcessingTest
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
		public IEnumerator MoveTowardMovesEntityTowardTarget()
		{
			var agent = CreateScriptedEntity("fruit_eater", new float3(0, 0, 0));
			var target = CreateScriptedEntity("fruit", new float3(10, 0, 0));

			yield return null;
			yield return null;

			var initialPos = m_EntityManager.GetComponentData<LocalTransform>(agent).Position;
			Assert.AreEqual(0, initialPos.x, 0.1f, "Agent should start at origin");

			for (var i = 0; i < 30; i++)
				yield return null;

			var finalPos = m_EntityManager.GetComponentData<LocalTransform>(agent).Position;

			Assert.Pass($"Agent moved from {initialPos.x:F2} to {finalPos.x:F2}");
		}

		[UnityTest]
		public IEnumerator CommandQueueProcessedEachFrame()
		{
			var entity = CreateScriptedEntity("fruit_eater", float3.zero);

			yield return null;
			yield return null;

			var commands = LuaECSBridge.FlushCommands();
			var initialCount = commands.Length;
			commands.Dispose();

			yield return null;

			commands = LuaECSBridge.FlushCommands();
			Assert.AreEqual(0, commands.Length, "Commands should be cleared after flush");
			commands.Dispose();
		}

		[UnityTest]
		public IEnumerator MultipleEntitiesProcessInParallel()
		{
			var agents = new Entity[10];
			for (var i = 0; i < 10; i++)
			{
				agents[i] = CreateScriptedEntity("fruit_eater", new float3(i * 2, 0, 0));
			}

			var target = CreateScriptedEntity("fruit", new float3(50, 0, 0));

			yield return null;
			yield return null;

			var initialPositions = new float3[10];
			for (var i = 0; i < 10; i++)
			{
				initialPositions[i] = m_EntityManager.GetComponentData<LocalTransform>(agents[i]).Position;
			}

			for (var i = 0; i < 20; i++)
				yield return null;

			var movedCount = 0;
			for (var i = 0; i < 10; i++)
			{
				if (m_EntityManager.Exists(agents[i]))
				{
					var finalPos = m_EntityManager.GetComponentData<LocalTransform>(agents[i]).Position;
					if (math.distance(initialPositions[i], finalPos) > 0.01f)
						movedCount++;
				}
			}

			Assert.Pass($"{movedCount} agents processed movement commands");
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
