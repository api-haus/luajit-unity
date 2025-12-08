namespace LuaECS.Systems
{
	using System.Collections.Generic;
	using LuaCharacters;
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaECS.Systems.Support;
	using LuaVM.Core;
	using Unity.CharacterController;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics;
	using Unity.Physics;
	using Unity.Transforms;

	public struct LuaScriptingSystemSingleton : IComponentData { }

	/// <summary>
	/// ECB system for deferred Lua entity operations.
	/// Plays back at end of SimulationSystemGroup.
	/// </summary>
	[UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
	public partial class LuaEntityCommandBufferSystem : EntityCommandBufferSystem
	{
		protected override void OnCreate()
		{
			base.OnCreate();
		}
	}

	/// <summary>
	/// Orchestrates Lua script runtime: updates and events.
	/// Script initialization is handled by LuaScriptFulfillmentSystem in InitializationSystemGroup.
	/// </summary>
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateBefore(typeof(LuaEntityCommandBufferSystem))]
	public partial class LuaScriptingSystem : SystemBase
	{
		LuaVMManager m_VM;
		LuaEntityCommandBufferSystem m_ECBSystem;
		LuaScriptFulfillmentSystem m_FulfillmentSystem;

		LuaEventDispatcher m_EventDispatcher;

		EntityCommandBuffer m_CurrentECB;
		bool m_ECBValid;

		ComponentLookup<LocalTransform> m_TransformLookup;
		BufferLookup<LuaScript> m_ScriptBufferLookup;

		ComponentLookup<LuaCharacterControl> m_CharacterControlLookup;
		ComponentLookup<KinematicCharacterBody> m_CharacterBodyLookup;
		ComponentLookup<PhysicsVelocity> m_PhysicsVelocityLookup;

		List<(
			Entity entity,
			int scriptIndex,
			string scriptName,
			int entityIndex,
			int stateRef
		)> m_PendingUpdates;

		static int s_FrameCount;

		protected override void OnCreate()
		{
			m_ECBSystem = World.GetOrCreateSystemManaged<LuaEntityCommandBufferSystem>();
			m_FulfillmentSystem = World.GetOrCreateSystemManaged<LuaScriptFulfillmentSystem>();

			var eventQuery = GetEntityQuery(
				ComponentType.ReadWrite<LuaScript>(),
				ComponentType.ReadWrite<LuaEvent>(),
				ComponentType.ReadOnly<LuaEntityId>()
			);

			m_EventDispatcher = new LuaEventDispatcher(
				null,
				m_FulfillmentSystem.EntityIdManager,
				EntityManager,
				eventQuery
			);

			m_PendingUpdates = new List<(Entity, int, string, int, int)>(256);
			m_TransformLookup = GetComponentLookup<LocalTransform>();
			m_ScriptBufferLookup = GetBufferLookup<LuaScript>(true);

			m_CharacterControlLookup = GetComponentLookup<LuaCharacterControl>();
			m_CharacterBodyLookup = GetComponentLookup<KinematicCharacterBody>(true);
			m_PhysicsVelocityLookup = GetComponentLookup<PhysicsVelocity>(true);
		}

		protected override void OnStartRunning()
		{
			if (m_VM == null)
			{
				m_VM = LuaVMManager.GetOrCreate();

				m_VM.RegisterBridgeNow(LuaECSBridge.RegisterFunctions);

				m_EventDispatcher.SetVM(m_VM);
				LuaECSBridge.Initialize(World, this);
			}

			EntityManager.CreateSingleton<LuaScriptingSystemSingleton>();
		}

		protected override void OnDestroy()
		{
			LuaECSBridge.Shutdown();
			m_VM?.Dispose();
			m_VM = null;
		}

		protected override void OnUpdate()
		{
			if (m_VM == null || !m_VM.IsValid)
				return;

			s_FrameCount++;

			m_CurrentECB = m_ECBSystem.CreateCommandBuffer();
			m_ECBValid = true;

			m_TransformLookup.Update(this);
			m_ScriptBufferLookup.Update(this);
			m_CharacterControlLookup.Update(this);
			m_CharacterBodyLookup.Update(this);
			m_PhysicsVelocityLookup.Update(this);

			LuaECSBridge.UpdateBurstContext(m_TransformLookup, m_ScriptBufferLookup);
			LuaECSBridge.UpdateCharacterContext(
				m_CharacterControlLookup,
				m_CharacterBodyLookup,
				m_PhysicsVelocityLookup
			);

			UpdateScriptedEntities();
			DispatchEvents();
			ProcessPendingOperations();

			m_ECBValid = false;
		}

		void ProcessPendingOperations()
		{
			var entityIdManager = m_FulfillmentSystem.EntityIdManager;

			var creations = LuaECSBridge.FlushCreations();
			for (var i = 0; i < creations.Length; i++)
			{
				var creation = creations[i];
				entityIdManager.CreateEntityWithId(creation.EntityId, creation.Position, m_CurrentECB);
			}
			creations.Dispose();

			var scripts = LuaECSBridge.FlushScriptAdditions();
			for (var i = 0; i < scripts.Length; i++)
			{
				var script = scripts[i];
				entityIdManager.AddScriptDeferred(
					script.EntityId,
					script.ScriptName.ToString(),
					m_CurrentECB
				);
			}
			scripts.Dispose();

			var destructions = LuaECSBridge.FlushDestructions();
			for (var i = 0; i < destructions.Length; i++)
				entityIdManager.DestroyEntityDeferred(destructions[i], m_CurrentECB);
			destructions.Dispose();
		}

		void UpdateScriptedEntities()
		{
			var deltaTime = SystemAPI.Time.DeltaTime;
			m_PendingUpdates.Clear();

			foreach (
				var (scripts, entity) in SystemAPI
					.Query<DynamicBuffer<LuaScript>>()
					.WithAll<LuaEntityId>()
					.WithEntityAccess()
			)
			{
				for (var i = 0; i < scripts.Length; i++)
				{
					var script = scripts[i];
					if (script.StateRef >= 0 && !script.Disabled)
					{
						m_PendingUpdates.Add(
							(entity, i, script.ScriptName.ToString(), script.EntityIndex, script.StateRef)
						);
					}
				}
			}

			foreach (var (entity, scriptIndex, scriptName, entityIndex, stateRef) in m_PendingUpdates)
			{
				if (!EntityManager.Exists(entity))
				{
					Log.Warning("[LuaScripting] Entity no longer exists for script {0}", scriptName);
					continue;
				}

				if (!EntityManager.HasComponent<LuaEntityId>(entity))
				{
					continue;
				}

				if (stateRef < 0)
				{
					Log.Error(
						"[LuaScripting] Invalid stateRef={0} for {1} entity={2}",
						stateRef,
						scriptName,
						entityIndex
					);
					continue;
				}

				if (!m_VM.ValidateStateRef(scriptName, entityIndex, stateRef))
				{
					Log.Warning(
						"[LuaScripting] StateRef mismatch for {0} entity={1} stateRef={2} - skipping update",
						scriptName,
						entityIndex,
						stateRef
					);
					continue;
				}

				m_VM.CallUpdate(scriptName, entityIndex, stateRef, deltaTime);
			}
		}

		void DispatchEvents()
		{
			m_EventDispatcher.CollectPendingEvents();
			m_EventDispatcher.ClearEventBuffers(m_CurrentECB);
			m_EventDispatcher.DispatchEvents();
		}

		#region Public API

		public LuaEntityCollection EntityCollection => m_FulfillmentSystem.EntityCollection;

		public int CreateEntityDeferred(float3 position)
		{
			if (!m_ECBValid)
			{
				Log.Error("[LuaScripting] CreateEntityDeferred called outside of update");
				return -1;
			}
			return m_FulfillmentSystem.EntityIdManager.CreateEntityDeferred(position, m_CurrentECB);
		}

		public bool AddScriptDeferred(int entityId, string scriptName)
		{
			if (!m_ECBValid)
			{
				Log.Error("[LuaScripting] AddScriptDeferred called outside of update");
				return false;
			}
			return m_FulfillmentSystem.EntityIdManager.AddScriptDeferred(
				entityId,
				scriptName,
				m_CurrentECB
			);
		}

		public void SetPositionDeferred(int entityId, float3 position)
		{
			if (!m_ECBValid)
			{
				Log.Error("[LuaScripting] SetPositionDeferred called outside of update");
				return;
			}
			m_FulfillmentSystem.EntityIdManager.SetPositionDeferred(entityId, position, m_CurrentECB);
		}

		public void DestroyEntityDeferred(int entityId)
		{
			if (!m_ECBValid)
			{
				Log.Error("[LuaScripting] DestroyEntityDeferred called outside of update");
				return;
			}
			m_FulfillmentSystem.EntityIdManager.DestroyEntityDeferred(entityId, m_CurrentECB);
		}

		public int GetEntityIdFromEntity(Entity entity)
		{
			return m_FulfillmentSystem.GetEntityIdFromEntity(entity);
		}

		public Entity GetEntityFromId(int entityId)
		{
			return m_FulfillmentSystem.GetEntityFromId(entityId);
		}

		public bool IsDeferred(int entityId)
		{
			return m_FulfillmentSystem.EntityIdManager.IsDeferred(entityId);
		}

		public void SendCommand(Entity entity, string command)
		{
			if (!EntityManager.HasBuffer<LuaScript>(entity))
				return;

			var scripts = EntityManager.GetBuffer<LuaScript>(entity);
			for (var i = 0; i < scripts.Length; i++)
			{
				var script = scripts[i];
				if (script.StateRef < 0 || script.Disabled)
					continue;

				var scriptName = script.ScriptName.ToString();
				m_VM.CallCommand(scriptName, script.EntityIndex, script.StateRef, command);
			}
		}

		#endregion
	}
}
