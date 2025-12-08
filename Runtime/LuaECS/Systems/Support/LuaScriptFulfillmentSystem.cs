namespace LuaECS.Systems.Support
{
	using LuaECS.Components;
	using LuaECS.Core;
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
		LuaVMManager m_VM;
		LuaEntityIdManager m_EntityIdManager;
		EntityQuery m_RequestQuery;

		public LuaEntityIdManager EntityIdManager => m_EntityIdManager;

		protected override void OnCreate()
		{
			m_EntityIdManager = new LuaEntityIdManager(EntityManager);
			m_RequestQuery = GetEntityQuery(ComponentType.ReadWrite<LuaScriptRequest>());
		}

		protected override void OnDestroy()
		{
			m_EntityIdManager?.Dispose();
		}

		protected override void OnStartRunning()
		{
			m_VM = LuaVMManager.Instance ?? LuaVMManager.GetOrCreate();
		}

		protected override void OnUpdate()
		{
			m_EntityIdManager.BeginFrame();

			if (m_VM == null || !m_VM.IsValid)
			{
				if (LuaVMManager.Instance != null && LuaVMManager.Instance.IsValid)
					m_VM = LuaVMManager.Instance;
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
					if (!requests[i].Fulfilled)
					{
						hasUnfulfilled = true;
						break;
					}
				}

				if (!hasUnfulfilled)
					continue;

				var entityId = m_EntityIdManager.GetOrAssignEntityId(entity, ecb);

				if (!EntityManager.HasBuffer<LuaScript>(entity))
				{
					EntityManager.AddBuffer<LuaScript>(entity);
					requests = EntityManager.GetBuffer<LuaScriptRequest>(entity);
				}

				var scripts = EntityManager.GetBuffer<LuaScript>(entity);

				for (var i = 0; i < requests.Length; i++)
				{
					var request = requests[i];
					if (request.Fulfilled)
						continue;

					if (HasScriptWithHash(scripts, request.RequestHash))
					{
						request.Fulfilled = true;
						requests[i] = request;
						Log.Verbose("[LuaFulfillment] Skipping duplicate request hash for entity {0}", entity);
						continue;
					}

					var scriptName = LuaScriptPathUtility.NormalizeScriptId(request.ScriptName.ToString());

					if (string.IsNullOrEmpty(scriptName))
					{
						Log.Error("[LuaFulfillment] Script name is empty for entity {0}", entity);
						request.Fulfilled = true;
						requests[i] = request;
						continue;
					}

					if (!LuaScriptPathUtility.ScriptExists(scriptName))
					{
						Log.Error(
							"[LuaFulfillment] Script file missing: {0}",
							LuaScriptPathUtility.GetScriptFilePath(scriptName)
						);
						request.Fulfilled = true;
						requests[i] = request;
						continue;
					}

					if (!m_VM.LoadScript(scriptName))
					{
						Log.Error("[LuaFulfillment] Failed to load script: {0}", scriptName);
						request.Fulfilled = true;
						requests[i] = request;
						continue;
					}

					var stateRef = m_VM.CreateEntityState(scriptName, entityId);
					if (stateRef < 0)
					{
						Log.Error(
							"[LuaFulfillment] CreateEntityState failed for {0} entity={1}",
							scriptName,
							entityId
						);
						request.Fulfilled = true;
						requests[i] = request;
						continue;
					}

					scripts = EntityManager.GetBuffer<LuaScript>(entity);
					scripts.Add(
						new LuaScript
						{
							ScriptName = new FixedString64Bytes(scriptName),
							StateRef = stateRef,
							EntityIndex = entityId,
							RequestHash = request.RequestHash,
							Disabled = false,
						}
					);

					Log.Verbose(
						"[LuaFulfillment] Fulfilled script '{0}' on entity {1}, stateRef={2}",
						scriptName,
						entityId,
						stateRef
					);

					m_VM.CallInit(scriptName, entityId, stateRef);

					requests = EntityManager.GetBuffer<LuaScriptRequest>(entity);
					request.Fulfilled = true;
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
				if (scripts[i].RequestHash == hash)
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
				if (script.ScriptName.ToString() == scriptName && !script.Disabled && script.StateRef >= 0)
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
				if (script.RequestHash == hash && !script.Disabled && script.StateRef >= 0)
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
			var scriptName = script.ScriptName.ToString();

			if (m_VM != null && m_VM.IsValid)
			{
				m_VM.CallFunction(scriptName, "OnDestroy", script.EntityIndex, script.StateRef);
				m_VM.ReleaseEntityState(scriptName, script.EntityIndex, script.StateRef);
			}

			Log.Verbose(
				"[LuaFulfillment] Disabled script '{0}' on entity {1}",
				scriptName,
				script.EntityIndex
			);

			script.StateRef = -1;
			script.Disabled = true;
			scripts[index] = script;
		}

		public int GetEntityIdFromEntity(Entity entity) =>
			m_EntityIdManager.GetEntityIdFromEntity(entity);

		public Entity GetEntityFromId(int entityId) => m_EntityIdManager.GetEntityFromId(entityId);

		public LuaEntityCollection EntityCollection => m_EntityIdManager.Collection;
	}
}
