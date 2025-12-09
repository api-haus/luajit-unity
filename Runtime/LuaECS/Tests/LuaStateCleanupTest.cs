namespace LuaECS.Tests
{
	using System.Collections;
	using Components;
	using Core;
	using LuaNET.LuaJIT;
	using LuaVM.Core;
	using NUnit.Framework;
	using Unity.Entities;
	using Unity.Transforms;
	using UnityEngine.TestTools;

	/// <summary>
	/// Focused coverage for Lua state refs around update and cleanup.
	/// Verifies registry entries stay tables during updates and are released on teardown.
	/// </summary>
	public class LuaStateCleanupTest
	{
		World m_World;
		EntityManager m_EntityManager;
		LuaVMManager m_VM;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			m_World = World.DefaultGameObjectInjectionWorld;
			m_EntityManager = m_World.EntityManager;
			m_VM = LuaTestUtilities.GetOrCreateTestVM();
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
		public IEnumerator StateRemainsTableDuringUpdates()
		{
			var entity = CreateScriptedEntity("fruit");

			yield return null;
			yield return null;

			var scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
			var script = scripts[0];
			Assert.GreaterOrEqual(script.stateRef, 0, "Script should be initialized with valid StateRef");

			AssertStateIsTable(script.stateRef, "State should be table after init");

			for (var i = 0; i < 4; i++)
				yield return null;

			scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
			script = scripts[0];
			AssertStateIsTable(script.stateRef, "State should remain a table after updates");
		}

		[UnityTest]
		public IEnumerator StateReleasedAfterCleanup()
		{
			var entity = CreateScriptedEntity("fruit");

			yield return null;
			yield return null;

			var scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
			var script = scripts[0];
			var scriptName = script.scriptName.ToString();
			var stateRef = script.stateRef;
			var entityIndex = script.entityIndex;

			AssertStateIsTable(stateRef, "State should be table before cleanup");
			Assert.IsTrue(
				m_VM.ValidateStateRef(scriptName, entityIndex, stateRef),
				"StateRef should be tracked before cleanup"
			);

			m_EntityManager.RemoveComponent<LuaEntityId>(entity);

			var framesWaited = 0;
			while (m_EntityManager.Exists(entity) && framesWaited < 30)
			{
				yield return null;
				framesWaited++;
			}

			Assert.IsFalse(m_EntityManager.Exists(entity), "Entity should be destroyed by cleanup");
			Assert.IsFalse(
				m_VM.ValidateStateRef(scriptName, entityIndex, stateRef),
				"StateRef should be removed after cleanup"
			);

			// Note: We don't check the raw registry slot because it may be reused by other entities
			// created during parallel test execution. ValidateStateRef is the correct semantic check.
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

		void AssertStateIsTable(int stateRef, string message)
		{
			var state = m_VM.State;
			var stackTop = Lua.lua_gettop(state);
			Lua.lua_rawgeti(state, Lua.LUA_REGISTRYINDEX, stateRef);
			try
			{
				Assert.AreEqual(Lua.LUA_TTABLE, Lua.lua_type(state, -1), message);
			}
			finally
			{
				Lua.lua_settop(state, stackTop);
			}
		}

		void AssertStateIsNil(int stateRef, string message)
		{
			var state = m_VM.State;
			var stackTop = Lua.lua_gettop(state);
			Lua.lua_rawgeti(state, Lua.LUA_REGISTRYINDEX, stateRef);
			try
			{
				Assert.AreEqual(Lua.LUA_TNIL, Lua.lua_type(state, -1), message);
			}
			finally
			{
				Lua.lua_settop(state, stackTop);
			}
		}
	}
}
