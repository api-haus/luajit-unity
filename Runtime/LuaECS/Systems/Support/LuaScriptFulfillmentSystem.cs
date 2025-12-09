namespace LuaECS.Systems.Support
{
	using Components;
	using Core;
	using LuaVM.Core;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;

	/// <summary>
	/// Processes script requests and creates initialized scripts.
	/// Runs in InitializationSystemGroup to ensure scripts are ready before simulation.
	/// </summary>
	[UpdateInGroup(typeof(InitializationSystemGroup))]
	public partial class LuaScriptFulfillmentSystem : SystemBase
	{
		LuaVMManager m_Vm;
		EntityQuery m_RequestQuery;

		protected override void OnCreate()
		{
			LuaEntityRegistry.Initialize();
			m_RequestQuery = GetEntityQuery(ComponentType.ReadWrite<LuaScriptRequest>());
		}

		protected override void OnDestroy()
		{
			LuaEntityRegistry.Dispose();
		}

		protected override void OnStartRunning()
		{
			m_Vm = LuaVMManager.Instance ?? LuaVMManager.GetOrCreate();

			// Register ECS bridge - must happen before any scripts are loaded
			m_Vm.RegisterBridgeNow(LuaECSBridge.RegisterFunctions);
		}

		protected override void OnUpdate()
		{
			LuaEntityRegistry.BeginFrame(EntityManager);

			if (m_Vm == null || !m_Vm.IsValid)
			{
				if (LuaVMManager.Instance != null && LuaVMManager.Instance.IsValid)
					m_Vm = LuaVMManager.Instance;
				else
					return;
			}

			if (m_RequestQuery.IsEmptyIgnoreFilter)
				return;

			using var ecb = new EntityCommandBuffer(Allocator.Temp);
			var entities = m_RequestQuery.ToEntityArray(Allocator.Temp);

			foreach (var entity in entities)
			{
				if (!EntityManager.Exists(entity))
					continue;

				if (!EntityManager.HasComponent<LuaEntityId>(entity))
					continue;

				var requests = EntityManager.GetBuffer<LuaScriptRequest>(entity);
				if (requests.Length == 0)
					continue;

				var hasUnfulfilled = false;
				for (var i = 0; i < requests.Length; i++)
				{
					if (!requests[i].fulfilled)
					{
						hasUnfulfilled = true;
						break;
					}
				}

				if (!hasUnfulfilled)
					continue;

				var entityId = LuaEntityRegistry.GetOrAssignEntityId(entity, ecb, EntityManager);

				if (!EntityManager.HasBuffer<LuaScript>(entity))
				{
					EntityManager.AddBuffer<LuaScript>(entity);
					requests = EntityManager.GetBuffer<LuaScriptRequest>(entity);
				}

				var scripts = EntityManager.GetBuffer<LuaScript>(entity);

				for (var i = 0; i < requests.Length; i++)
				{
					var request = requests[i];
					if (request.fulfilled)
						continue;

					if (HasScriptWithHash(scripts, request.requestHash))
					{
						request.fulfilled = true;
						requests[i] = request;
						Log.Verbose("[LuaFulfillment] Skipping duplicate request hash for entity {0}", entity);
						continue;
					}

					var scriptName = LuaScriptPathUtility.NormalizeScriptId(request.scriptName.ToString());

					if (string.IsNullOrEmpty(scriptName))
					{
						Log.Error("[LuaFulfillment] Script name is empty for entity {0}", entity);
						request.fulfilled = true;
						requests[i] = request;
						continue;
					}

					if (!LuaScriptPathUtility.ScriptExists(scriptName))
					{
						Log.Error(
							"[LuaFulfillment] Script file missing: {0}",
							LuaScriptPathUtility.GetScriptFilePath(scriptName)
						);
						request.fulfilled = true;
						requests[i] = request;
						continue;
					}

					if (!m_Vm.LoadScript(scriptName))
					{
						Log.Error("[LuaFulfillment] Failed to load script: {0}", scriptName);
						request.fulfilled = true;
						requests[i] = request;
						continue;
					}

					// Parse script annotations for tick group
					var scriptPath = LuaScriptPathUtility.GetScriptFilePath(scriptName);
					var annotations = LuaScriptAnnotationParser.ParseFile(scriptPath);

					var stateRef = m_Vm.CreateEntityState(scriptName, entityId);
					if (stateRef < 0)
					{
						Log.Error(
							"[LuaFulfillment] CreateEntityState failed for {0} entity={1}",
							scriptName,
							entityId
						);
						request.fulfilled = true;
						requests[i] = request;
						continue;
					}

					scripts = EntityManager.GetBuffer<LuaScript>(entity);
					scripts.Add(
						new LuaScript
						{
							scriptName = new FixedString64Bytes(scriptName),
							stateRef = stateRef,
							entityIndex = entityId,
							requestHash = request.requestHash,
							disabled = false,
							tickGroup = annotations.tickGroup,
						}
					);

					Log.Verbose(
						"[LuaFulfillment] Fulfilled script '{0}' on entity {1}, stateRef={2}",
						scriptName,
						entityId,
						stateRef
					);

					m_Vm.CallInit(scriptName, entityId, stateRef);

					requests = EntityManager.GetBuffer<LuaScriptRequest>(entity);
					request.fulfilled = true;
					requests[i] = request;
				}
			}

			entities.Dispose();
			ecb.Playback(EntityManager);
		}

		static bool HasScriptWithHash(DynamicBuffer<LuaScript> scripts, Hash128 hash)
		{
			for (var i = 0; i < scripts.Length; i++)
			{
				if (scripts[i].requestHash == hash)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Disables a script by name. Calls OnDestroy, releases state, marks as disabled.
		/// Script remains in buffer until entity destruction.
		/// </summary>
		public bool DisableScript(Entity entity, string scriptName)
		{
			if (!EntityManager.HasBuffer<LuaScript>(entity))
				return false;

			var scripts = EntityManager.GetBuffer<LuaScript>(entity);
			for (var i = 0; i < scripts.Length; i++)
			{
				var script = scripts[i];
				if (script.scriptName.ToString() == scriptName && !script.disabled && script.stateRef >= 0)
				{
					DisableScriptAtIndex(entity, scripts, i);
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Disables a script by request hash. Calls OnDestroy, releases state, marks as disabled.
		/// Script remains in buffer until entity destruction.
		/// </summary>
		public bool DisableScriptByHash(Entity entity, Hash128 hash)
		{
			if (!EntityManager.HasBuffer<LuaScript>(entity))
				return false;

			var scripts = EntityManager.GetBuffer<LuaScript>(entity);
			for (var i = 0; i < scripts.Length; i++)
			{
				var script = scripts[i];
				if (script.requestHash == hash && !script.disabled && script.stateRef >= 0)
				{
					DisableScriptAtIndex(entity, scripts, i);
					return true;
				}
			}
			return false;
		}

		void DisableScriptAtIndex(Entity entity, DynamicBuffer<LuaScript> scripts, int index)
		{
			var script = scripts[index];
			var scriptName = script.scriptName.ToString();

			if (m_Vm != null && m_Vm.IsValid)
			{
				m_Vm.CallFunction(scriptName, "OnDestroy", script.entityIndex, script.stateRef);
				m_Vm.ReleaseEntityState(scriptName, script.entityIndex, script.stateRef);
			}

			Log.Verbose(
				"[LuaFulfillment] Disabled script '{0}' on entity {1}",
				scriptName,
				script.entityIndex
			);

			script.stateRef = -1;
			script.disabled = true;
			scripts[index] = script;
		}

		public int GetEntityIdFromEntity(Entity entity) =>
			LuaEntityRegistry.GetEntityIdFromEntity(entity, EntityManager);

		public Entity GetEntityFromId(int entityId) => LuaEntityRegistry.GetEntityFromId(entityId);
	}
}
