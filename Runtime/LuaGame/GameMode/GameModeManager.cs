namespace LuaGame.GameMode
{
	using System;
	using System.Threading;
	using System.Threading.Tasks;
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaECS.Systems;
	using LuaECS.Systems.Support;
	using LuaVM.Core;
	using Unity.Entities;
	using Unity.Logging;

	/// <summary>
	/// Manages game mode lifecycle: loading, transitions, world ownership.
	/// Each game mode creates and owns its own ECS World instance.
	/// </summary>
	public class GameModeManager : IDisposable
	{
		bool m_BridgeRegistered;
		/// <summary>
		/// Current game mode name, or null if no mode active.
		/// </summary>
		public string CurrentMode { get; private set; }

		/// <summary>
		/// Current mode's ECS World, or null if no mode active.
		/// </summary>
		public World CurrentWorld { get; private set; }

		/// <summary>
		/// True if a mode transition is in progress.
		/// </summary>
		public bool IsLoading { get; private set; }

		/// <summary>
		/// Loading progress 0.0 to 1.0 during transitions.
		/// </summary>
		public float LoadingProgress { get; private set; }

		/// <summary>
		/// Event fired when mode loading starts.
		/// </summary>
		public event Action<string> OnModeLoadStarted;

		/// <summary>
		/// Event fired when mode is fully loaded.
		/// </summary>
		public event Action<string> OnModeLoaded;

		/// <summary>
		/// Event fired before mode unloads.
		/// </summary>
		public event Action<string> OnModeUnloading;

		Entity m_ModeEntity;
		bool m_Disposed;

		void EnsureBridgeRegistered()
		{
			if (m_BridgeRegistered)
				return;

			var vm = LuaVMManager.GetOrCreate();
			vm.RegisterBridgeNow(LuaGameModeBridge.RegisterFunctions);
			LuaGameModeBridge.SetManager(this);
			m_BridgeRegistered = true;
		}

		/// <summary>
		/// Load a game mode by script name.
		/// </summary>
		/// <param name="modeName">Script name without path/extension (e.g., "roguelike")</param>
		/// <param name="ct">Cancellation token</param>
		public Task LoadModeAsync(string modeName, CancellationToken ct = default)
		{
			return LoadModeAsync(modeName, null, ct);
		}

		/// <summary>
		/// Load a game mode with progress callback.
		/// </summary>
		public async Task LoadModeAsync(string modeName, IProgress<float> progress, CancellationToken ct = default)
		{
			if (m_Disposed)
				throw new ObjectDisposedException(nameof(GameModeManager));

			if (IsLoading)
				throw new InvalidOperationException("A mode transition is already in progress");

			if (string.IsNullOrEmpty(modeName))
				throw new ArgumentException("Mode name cannot be null or empty", nameof(modeName));

			IsLoading = true;
			LoadingProgress = 0f;

			try
			{
				// Ensure bridge is registered before any mode operations
				EnsureBridgeRegistered();

				OnModeLoadStarted?.Invoke(modeName);
				progress?.Report(0f);

				// Phase 1: Unload current mode (0-20%)
				if (CurrentWorld != null)
				{
					await UnloadCurrentModeInternalAsync(ct);
				}
				LoadingProgress = 0.2f;
				progress?.Report(0.2f);

				ct.ThrowIfCancellationRequested();

				// Phase 2: Create new world (20-40%)
				CurrentWorld = CreateModeWorld(modeName);
				CurrentMode = modeName;
				LoadingProgress = 0.4f;
				progress?.Report(0.4f);

				ct.ThrowIfCancellationRequested();

				// Phase 3: Create mode entity with script (40-60%)
				m_ModeEntity = CreateModeEntity(modeName);
				LoadingProgress = 0.6f;
				progress?.Report(0.6f);

				// Phase 4: Wait for systems to initialize and run OnInit (60-80%)
				// First update triggers LuaScriptFulfillmentSystem to process the script request
				CurrentWorld.Update();
				LoadingProgress = 0.8f;
				progress?.Report(0.8f);

				ct.ThrowIfCancellationRequested();

				// Phase 5: OnLoad callback (80-100%)
				// Second update ensures OnInit completed, then call OnLoad
				CurrentWorld.Update();
				CallOnLoad();
				LoadingProgress = 1f;
				progress?.Report(1f);

				Log.Info("[GameMode] Loaded mode: {0}", modeName);
				OnModeLoaded?.Invoke(modeName);
			}
			catch (OperationCanceledException)
			{
				// Cleanup partial state on cancellation
				if (CurrentWorld != null)
				{
					DisposeCurrentWorld();
				}
				CurrentMode = null;
				throw;
			}
			finally
			{
				IsLoading = false;
			}
		}

		/// <summary>
		/// Unload current mode without loading another.
		/// </summary>
		public async Task UnloadCurrentModeAsync(CancellationToken ct = default)
		{
			if (m_Disposed)
				throw new ObjectDisposedException(nameof(GameModeManager));

			if (IsLoading)
				throw new InvalidOperationException("A mode transition is already in progress");

			if (CurrentWorld == null)
				return;

			IsLoading = true;
			LoadingProgress = 0f;

			try
			{
				await UnloadCurrentModeInternalAsync(ct);
				LoadingProgress = 1f;
			}
			finally
			{
				IsLoading = false;
			}
		}

		async Task UnloadCurrentModeInternalAsync(CancellationToken ct)
		{
			if (CurrentWorld == null)
				return;

			var modeName = CurrentMode;
			OnModeUnloading?.Invoke(modeName);

			// Call OnBeforeUnload on mode script
			CallOnBeforeUnload();

			// Allow frame for cleanup callbacks
			await Task.Yield();
			ct.ThrowIfCancellationRequested();

			// Dispose world and clear state
			DisposeCurrentWorld();
			CurrentMode = null;
			m_ModeEntity = Entity.Null;

			Log.Info("[GameMode] Unloaded mode: {0}", modeName);
		}

		World CreateModeWorld(string modeName)
		{
			var world = new World(modeName);

			// Get or create system groups
			world.GetOrCreateSystemManaged<InitializationSystemGroup>();
			world.GetOrCreateSystemManaged<SimulationSystemGroup>();

			// Add Lua systems - they will self-activate via RequireForUpdate
			// InitializationSystemGroup systems
			var initGroup = world.GetExistingSystemManaged<InitializationSystemGroup>();
			initGroup.AddSystemToUpdateList(world.CreateSystemManaged<LuaScriptFulfillmentSystem>());
			initGroup.AddSystemToUpdateList(world.CreateSystemManaged<LuaScriptCleanupSystem>());

			// SimulationSystemGroup systems
			var simGroup = world.GetExistingSystemManaged<SimulationSystemGroup>();
			simGroup.AddSystemToUpdateList(world.CreateSystemManaged<LuaScriptingSystem>());

			// EndSimulationEntityCommandBufferSystem is needed for deferred operations
			simGroup.AddSystemToUpdateList(world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>());

			initGroup.SortSystems();
			simGroup.SortSystems();

			return world;
		}

		Entity CreateModeEntity(string modeName)
		{
			var entityManager = CurrentWorld.EntityManager;
			var entity = entityManager.CreateEntity();

			// Add script request for the game mode script
			var requestBuffer = entityManager.AddBuffer<LuaScriptRequest>(entity);
			requestBuffer.Add(new LuaScriptRequest
			{
				scriptName = $"GameModes/{modeName}",
				requestHash = LuaScriptPathUtility.HashScriptName($"GameModes/{modeName}"),
				fulfilled = false,
			});

			// Add event buffer
			entityManager.AddBuffer<LuaEvent>(entity);

			return entity;
		}

		void CallOnLoad()
		{
			if (CurrentWorld == null || m_ModeEntity == Entity.Null)
				return;

			var entityManager = CurrentWorld.EntityManager;
			if (!entityManager.Exists(m_ModeEntity))
				return;

			if (!entityManager.HasBuffer<LuaScript>(m_ModeEntity))
				return;

			var scripts = entityManager.GetBuffer<LuaScript>(m_ModeEntity);
			if (scripts.Length == 0)
				return;

			var script = scripts[0];
			if (script.stateRef < 0 || script.disabled)
				return;

			var vm = LuaVMManager.Instance;
			if (vm == null || !vm.IsValid)
				return;

			vm.CallFunction(script.scriptName.ToString(), "OnLoad", script.entityIndex, script.stateRef);
		}

		void CallOnBeforeUnload()
		{
			if (CurrentWorld == null || m_ModeEntity == Entity.Null)
				return;

			var entityManager = CurrentWorld.EntityManager;
			if (!entityManager.Exists(m_ModeEntity))
				return;

			if (!entityManager.HasBuffer<LuaScript>(m_ModeEntity))
				return;

			var scripts = entityManager.GetBuffer<LuaScript>(m_ModeEntity);
			if (scripts.Length == 0)
				return;

			var script = scripts[0];
			if (script.stateRef < 0 || script.disabled)
				return;

			var vm = LuaVMManager.Instance;
			if (vm == null || !vm.IsValid)
				return;

			vm.CallFunction(script.scriptName.ToString(), "OnBeforeUnload", script.entityIndex, script.stateRef);
		}

		void DisposeCurrentWorld()
		{
			if (CurrentWorld == null)
				return;

			// Shutdown bridge before world disposal
			LuaECSBridge.Shutdown();

			// Dispose the world - this destroys all entities and systems
			CurrentWorld.Dispose();
			CurrentWorld = null;

			// Clear registry state (will be reinitialized by LuaScriptFulfillmentSystem.OnCreate)
			if (LuaEntityRegistry.IsCreated)
				LuaEntityRegistry.Dispose();
		}

		public void Dispose()
		{
			if (m_Disposed)
				return;

			m_Disposed = true;

			if (CurrentWorld != null)
			{
				CallOnBeforeUnload();
				DisposeCurrentWorld();
			}

			CurrentMode = null;
			m_ModeEntity = Entity.Null;
		}
	}
}
