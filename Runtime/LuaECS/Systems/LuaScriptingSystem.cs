namespace LuaECS.Systems
{
	using System.Collections.Generic;
	using Components;
	using Core;
	using LuaCharacters;
	using LuaCharacters.Core;
	using LuaVM.Core;
	using Support;
	using Unity.CharacterController;
	using Unity.Collections;
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
		LuaVMManager m_Vm;
		LuaEntityCommandBufferSystem m_ECBSystem;
		LuaScriptFulfillmentSystem m_FulfillmentSystem;

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

		EntityQuery m_EventQuery;

		static int s_frameCount;

		protected override void OnCreate()
		{
			m_ECBSystem = World.GetOrCreateSystemManaged<LuaEntityCommandBufferSystem>();
			m_FulfillmentSystem = World.GetOrCreateSystemManaged<LuaScriptFulfillmentSystem>();

			m_EventQuery = GetEntityQuery(
				ComponentType.ReadWrite<LuaScript>(),
				ComponentType.ReadWrite<LuaEvent>(),
				ComponentType.ReadOnly<LuaEntityId>()
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
			if (m_Vm == null)
			{
				m_Vm = LuaVMManager.GetOrCreate();

				m_Vm.RegisterBridgeNow(LuaECSBridge.RegisterFunctions);
				LuaECSBridge.Initialize(World, this);
			}

			EntityManager.CreateSingleton<LuaScriptingSystemSingleton>();
		}

		protected override void OnDestroy()
		{
			LuaECSBridge.Shutdown();
			m_Vm?.Dispose();
			m_Vm = null;
		}

		protected override void OnUpdate()
		{
			if (m_Vm == null || !m_Vm.IsValid)
				return;

			s_frameCount++;

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
			var creations = LuaECSBridge.FlushCreations();
			for (var i = 0; i < creations.Length; i++)
			{
				var creation = creations[i];
				LuaEntityRegistry.CreateWithId(creation.entityId, creation.position, m_CurrentECB);
			}
			creations.Dispose();

			var scripts = LuaECSBridge.FlushScriptAdditions();
			for (var i = 0; i < scripts.Length; i++)
			{
				var script = scripts[i];
				LuaEntityRegistry.AddScriptDeferred(
					script.entityId,
					script.scriptName.ToString(),
					m_CurrentECB,
					EntityManager
				);
			}
			scripts.Dispose();

			var destructions = LuaECSBridge.FlushDestructions();
			for (var i = 0; i < destructions.Length; i++)
				LuaEntityRegistry.DestroyEntityDeferred(destructions[i], m_CurrentECB);
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
					if (script.stateRef >= 0 && !script.disabled)
					{
						m_PendingUpdates.Add(
							(entity, i, script.scriptName.ToString(), script.entityIndex, script.stateRef)
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

				if (!m_Vm.ValidateStateRef(scriptName, entityIndex, stateRef))
				{
					Log.Warning(
						"[LuaScripting] StateRef mismatch for {0} entity={1} stateRef={2} - skipping update",
						scriptName,
						entityIndex,
						stateRef
					);
					continue;
				}

				m_Vm.CallUpdate(scriptName, entityIndex, stateRef, deltaTime);
			}
		}

		void DispatchEvents()
		{
			CollectPendingEvents();
			ClearEventBuffers();
			DispatchCollectedEvents();
		}

		void CollectPendingEvents()
		{
			LuaECSBridge.ClearEventContext();
			ref var ctx = ref LuaECSBridge.EventContext;
			if (!ctx.isValid)
				return;

			var entities = m_EventQuery.ToEntityArray(Allocator.Temp);
			foreach (var entity in entities)
			{
				var events = EntityManager.GetBuffer<LuaEvent>(entity);
				if (events.Length == 0)
					continue;

				LuaECSBridge.AddEntityToClear(entity);

				var eventStartIndex = ctx.eventBuffer.Length;
				for (var i = 0; i < events.Length; i++)
				{
					LuaECSBridge.AddEvent(events[i]);
				}
				var eventCount = events.Length;

				var scripts = EntityManager.GetBuffer<LuaScript>(entity);
				for (var i = 0; i < scripts.Length; i++)
				{
					var script = scripts[i];
					if (script.stateRef >= 0 && !script.disabled)
					{
						LuaECSBridge.AddEventDispatch(
							entity,
							i,
							script.scriptName,
							script.entityIndex,
							script.stateRef,
							eventStartIndex,
							eventCount
						);
					}
				}
			}
			entities.Dispose();
		}

		void ClearEventBuffers()
		{
			ref var ctx = ref LuaECSBridge.EventContext;
			if (!ctx.isValid)
				return;

			for (var i = 0; i < ctx.entitiesToClear.Length; i++)
			{
				m_CurrentECB.SetBuffer<LuaEvent>(ctx.entitiesToClear[i]);
			}
		}

		void DispatchCollectedEvents()
		{
			ref var ctx = ref LuaECSBridge.EventContext;
			if (!ctx.isValid)
				return;

			for (var i = 0; i < ctx.pendingEvents.Length; i++)
			{
				var dispatch = ctx.pendingEvents[i];

				if (!EntityManager.Exists(dispatch.entity))
					continue;

				if (!EntityManager.HasComponent<LuaEntityId>(dispatch.entity))
					continue;

				var scriptName = dispatch.scriptName.ToString();

				for (var j = 0; j < dispatch.eventCount; j++)
				{
					var evt = LuaECSBridge.GetEvent(dispatch.eventStartIndex + j);
					var eventName = evt.eventName.ToString();
					var sourceId = LuaEntityRegistry.GetEntityIdFromEntity(evt.source, EntityManager);
					var targetId = LuaEntityRegistry.GetEntityIdFromEntity(evt.target, EntityManager);

					m_Vm.CallEvent(
						scriptName,
						dispatch.entityIndex,
						dispatch.stateRef,
						eventName,
						sourceId,
						targetId,
						evt.intParam
					);
				}
			}
		}

		#region Public API

		public int CreateEntityDeferred(float3 position)
		{
			if (!m_ECBValid)
			{
				Log.Error("[LuaScripting] CreateEntityDeferred called outside of update");
				return -1;
			}
			return LuaEntityRegistry.Create(position, m_CurrentECB);
		}

		public bool AddScriptDeferred(int entityId, string scriptName)
		{
			if (!m_ECBValid)
			{
				Log.Error("[LuaScripting] AddScriptDeferred called outside of update");
				return false;
			}
			return LuaEntityRegistry.AddScriptDeferred(entityId, scriptName, m_CurrentECB, EntityManager);
		}

		public void SetPositionDeferred(int entityId, float3 position)
		{
			if (!m_ECBValid)
			{
				Log.Error("[LuaScripting] SetPositionDeferred called outside of update");
				return;
			}
			LuaEntityRegistry.SetPositionDeferred(entityId, position, m_CurrentECB);
		}

		public void DestroyEntityDeferred(int entityId)
		{
			if (!m_ECBValid)
			{
				Log.Error("[LuaScripting] DestroyEntityDeferred called outside of update");
				return;
			}
			LuaEntityRegistry.DestroyEntityDeferred(entityId, m_CurrentECB);
		}

		public int GetEntityIdFromEntity(Entity entity)
		{
			return LuaEntityRegistry.GetEntityIdFromEntity(entity, EntityManager);
		}

		public Entity GetEntityFromId(int entityId)
		{
			return LuaEntityRegistry.GetEntityFromId(entityId);
		}

		public bool IsDeferred(int entityId)
		{
			return LuaEntityRegistry.IsPending(entityId);
		}

		public void SendCommand(Entity entity, string command)
		{
			if (!EntityManager.HasBuffer<LuaScript>(entity))
				return;

			var scripts = EntityManager.GetBuffer<LuaScript>(entity);
			for (var i = 0; i < scripts.Length; i++)
			{
				var script = scripts[i];
				if (script.stateRef < 0 || script.disabled)
					continue;

				var scriptName = script.scriptName.ToString();
				m_Vm.CallCommand(scriptName, script.entityIndex, script.stateRef, command);
			}
		}

		#endregion
	}
}
