namespace LuaGame.Tests
{
	using System.Collections;
	using System.Threading.Tasks;
	using GameMode;
	using LuaECS.Components;
	using LuaECS.Core;
	using NUnit.Framework;
	using Unity.Entities;
	using UnityEngine.TestTools;

	/// <summary>
	/// PlayMode tests for GameModeManager.
	/// Verifies game mode loading, unloading, and world isolation.
	/// </summary>
	public class GameModeManagerTest
	{
		GameModeManager m_Manager;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			// Ensure clean state
			if (LuaEntityRegistry.IsCreated)
				LuaEntityRegistry.Dispose();

			m_Manager = new GameModeManager();
			yield return null;
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			m_Manager?.Dispose();
			m_Manager = null;

			// Cleanup any leftover registry state
			if (LuaEntityRegistry.IsCreated)
				LuaEntityRegistry.Dispose();

			yield return null;
		}

		[UnityTest]
		public IEnumerator LoadMode_CreatesNewWorld()
		{
			Assert.IsNull(m_Manager.CurrentWorld, "Should have no world before loading");
			Assert.IsNull(m_Manager.CurrentMode, "Should have no mode before loading");

			var loadTask = m_Manager.LoadModeAsync("fruit_eater");

			// Wait for async operation
			while (!loadTask.IsCompleted)
				yield return null;

			// Check for exceptions
			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			Assert.IsNotNull(m_Manager.CurrentWorld, "Should have world after loading");
			Assert.AreEqual("fruit_eater", m_Manager.CurrentMode, "Should have correct mode name");
			Assert.IsFalse(m_Manager.IsLoading, "Should not be loading after completion");
		}

		[UnityTest]
		public IEnumerator LoadMode_WorldContainsModeEntity()
		{
			var loadTask = m_Manager.LoadModeAsync("fruit_eater");
			while (!loadTask.IsCompleted)
				yield return null;

			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			var entityManager = m_Manager.CurrentWorld.EntityManager;

			// The mode entity has LuaScriptRequest - check for that since LuaScript
			// is only added after fulfillment system processes the request
			var requestQuery = entityManager.CreateEntityQuery(typeof(LuaScriptRequest));
			var requestCount = requestQuery.CalculateEntityCount();

			Assert.IsTrue(requestCount > 0, "World should contain entities with script requests after mode load");
		}

		[UnityTest]
		public IEnumerator UnloadMode_DisposesWorld()
		{
			var loadTask = m_Manager.LoadModeAsync("fruit_eater");
			while (!loadTask.IsCompleted)
				yield return null;

			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			var world = m_Manager.CurrentWorld;
			Assert.IsNotNull(world, "Should have world before unload");

			var unloadTask = m_Manager.UnloadCurrentModeAsync();
			while (!unloadTask.IsCompleted)
				yield return null;

			if (unloadTask.IsFaulted)
				throw unloadTask.Exception.InnerException;

			Assert.IsNull(m_Manager.CurrentWorld, "Should have no world after unload");
			Assert.IsNull(m_Manager.CurrentMode, "Should have no mode after unload");
			Assert.IsFalse(world.IsCreated, "Old world should be disposed");
		}

		[UnityTest]
		public IEnumerator SwitchModes_DisposesOldWorld()
		{
			// Load first mode
			var loadTask1 = m_Manager.LoadModeAsync("fruit_eater");
			while (!loadTask1.IsCompleted)
				yield return null;

			if (loadTask1.IsFaulted)
				throw loadTask1.Exception.InnerException;

			var firstWorld = m_Manager.CurrentWorld;
			Assert.IsNotNull(firstWorld, "Should have first world");

			// Load second mode (reusing same script for simplicity)
			var loadTask2 = m_Manager.LoadModeAsync("fruit_eater");
			while (!loadTask2.IsCompleted)
				yield return null;

			if (loadTask2.IsFaulted)
				throw loadTask2.Exception.InnerException;

			var secondWorld = m_Manager.CurrentWorld;
			Assert.IsNotNull(secondWorld, "Should have second world");
			Assert.AreNotSame(firstWorld, secondWorld, "Should be different world instances");
			Assert.IsFalse(firstWorld.IsCreated, "First world should be disposed");
			Assert.IsTrue(secondWorld.IsCreated, "Second world should be active");
		}

		[UnityTest]
		public IEnumerator LoadMode_CompletesSuccessfully()
		{
			// Test that loading completes and sets final state correctly
			var loadTask = m_Manager.LoadModeAsync("fruit_eater");

			while (!loadTask.IsCompleted)
				yield return null;

			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			// After completion, loading should be done
			Assert.IsFalse(m_Manager.IsLoading, "Should not be loading after completion");
			Assert.AreEqual(1f, m_Manager.LoadingProgress, 0.01f, "Final progress should be 1.0");
			Assert.IsNotNull(m_Manager.CurrentWorld, "Should have world after loading");
		}

		[UnityTest]
		public IEnumerator LoadMode_FiresEvents()
		{
			var loadStarted = false;
			var loadCompleted = false;
			string loadedModeName = null;

			m_Manager.OnModeLoadStarted += name => loadStarted = true;
			m_Manager.OnModeLoaded += name =>
			{
				loadCompleted = true;
				loadedModeName = name;
			};

			var loadTask = m_Manager.LoadModeAsync("fruit_eater");
			while (!loadTask.IsCompleted)
				yield return null;

			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			Assert.IsTrue(loadStarted, "OnModeLoadStarted should fire");
			Assert.IsTrue(loadCompleted, "OnModeLoaded should fire");
			Assert.AreEqual("fruit_eater", loadedModeName, "Should pass correct mode name to event");
		}

		[UnityTest]
		public IEnumerator UnloadMode_FiresEvent()
		{
			var loadTask = m_Manager.LoadModeAsync("fruit_eater");
			while (!loadTask.IsCompleted)
				yield return null;

			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			var unloadingFired = false;
			string unloadingModeName = null;

			m_Manager.OnModeUnloading += name =>
			{
				unloadingFired = true;
				unloadingModeName = name;
			};

			var unloadTask = m_Manager.UnloadCurrentModeAsync();
			while (!unloadTask.IsCompleted)
				yield return null;

			if (unloadTask.IsFaulted)
				throw unloadTask.Exception.InnerException;

			Assert.IsTrue(unloadingFired, "OnModeUnloading should fire");
			Assert.AreEqual("fruit_eater", unloadingModeName, "Should pass correct mode name to unload event");
		}

		[UnityTest]
		public IEnumerator GameModeWorldCanUpdate()
		{
			var loadTask = m_Manager.LoadModeAsync("fruit_eater");
			while (!loadTask.IsCompleted)
				yield return null;

			if (loadTask.IsFaulted)
				throw loadTask.Exception.InnerException;

			// Run for several frames - verify no errors occur
			for (var i = 0; i < 5; i++)
			{
				m_Manager.CurrentWorld.Update();
				yield return null;
			}

			// Verify world is still valid after updates
			Assert.IsTrue(m_Manager.CurrentWorld.IsCreated, "World should still be valid after updates");
			Assert.AreEqual("fruit_eater", m_Manager.CurrentMode, "Mode should still be active");
		}
	}
}
