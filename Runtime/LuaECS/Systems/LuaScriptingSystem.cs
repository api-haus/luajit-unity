namespace LuaECS.Systems
{
	using System.Collections.Generic;
	using Components;
	using Core;
	using LuaVM.Core;
	using Support;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics;
	using Unity.Transforms;

	public struct LuaScriptingSystemSingleton : IComponentData { }

	/// <summary>
	/// Orchestrates Lua script runtime: updates and events.
	/// Script initialization is handled by LuaScriptFulfillmentSystem in InitializationSystemGroup.
	/// Uses EndSimulationEntityCommandBufferSystem for deferred operations.
	/// </summary>
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
	public partial class LuaScriptingSystem : SystemBase
	{
		LuaVMManager m_Vm;
		LuaScriptFulfillmentSystem m_FulfillmentSystem;

		EntityCommandBuffer m_CurrentECB;
		bool m_ECBValid;

		EntityCommandBuffer m_PrimedECB;
		bool m_PrimedECBValid;

		ComponentLookup<LocalTransform> m_TransformLookup;
		BufferLookup<LuaScript> m_ScriptBufferLookup;

		List<(
			Entity entity,
			int scriptIndex,
			string scriptName,
			int entityIndex,
			int stateRef,
			LuaTickGroup tickGroup
		)> m_PendingUpdates;

		EntityQuery m_EventQuery;

		static int s_frameCount;

		protected override void OnCreate()
		{
			m_FulfillmentSystem = World.GetOrCreateSystemManaged<LuaScriptFulfillmentSystem>();

			m_EventQuery = GetEntityQuery(
				ComponentType.ReadWrite<LuaScript>(),
				ComponentType.ReadWrite<LuaEvent>(),
				ComponentType.ReadOnly<LuaEntityId>()
			);

			m_PendingUpdates = new List<(Entity, int, string, int, int, LuaTickGroup)>(256);
			m_TransformLookup = GetComponentLookup<LocalTransform>();
			m_ScriptBufferLookup = GetBufferLookup<LuaScript>(true);
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

		/// <summary>
		/// Primes the burst context with an ECB for OnInit entity creation.
		/// Call before the first world update to enable ecs.create_entity() during OnInit.
		/// </summary>
		public void PrimeBurstContextForOnInit()
		{
			// Create a persistent ECB that will be played back after the world update
			m_PrimedECB = new EntityCommandBuffer(Unity.Collections.Allocator.TempJob);
			m_PrimedECBValid = true;

			m_TransformLookup.Update(this);
			m_ScriptBufferLookup.Update(this);

			LuaECSBridge.UpdateBurstContext(m_PrimedECB, 0f, m_TransformLookup, m_ScriptBufferLookup);
		}

		/// <summary>
		/// Plays back the primed ECB after OnInit has completed.
		/// Call after the first world update to apply entity creations from OnInit.
		/// </summary>
		public void PlaybackPrimedECB()
		{
			if (m_PrimedECBValid && m_PrimedECB.IsCreated)
			{
				m_PrimedECB.Playback(EntityManager);
				m_PrimedECB.Dispose();
				m_PrimedECBValid = false;
			}
		}

		protected override void OnUpdate()
		{
			if (m_Vm == null || !m_Vm.IsValid)
				return;

			s_frameCount++;

			// Get ECB from Unity's EndSimulationEntityCommandBufferSystem
			var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
			m_CurrentECB = ecbSingleton.CreateCommandBuffer(World.Unmanaged);
			m_ECBValid = true;

			// Use SystemAPI.Time for default worlds, fall back to Unity time for custom worlds
			var deltaTime = SystemAPI.Time.DeltaTime;
			if (deltaTime <= 0f)
				deltaTime = UnityEngine.Time.deltaTime;

			// Complete any outstanding jobs reading/writing LocalTransform (e.g., physics)
			// before we allow Lua scripts to modify transforms directly
			EntityManager.CompleteDependencyBeforeRW<LocalTransform>();

			m_TransformLookup.Update(this);
			m_ScriptBufferLookup.Update(this);

			// Pass ECB and deltaTime to context for direct use by bridge functions
			LuaECSBridge.UpdateBurstContext(
				m_CurrentECB,
				deltaTime,
				m_TransformLookup,
				m_ScriptBufferLookup
			);
			UpdateScriptedEntities(deltaTime);
			DispatchEvents();
			// Note: ProcessPendingOperations removed - bridge functions now write directly to ECB

			m_ECBValid = false;
		}

		void UpdateScriptedEntities(float deltaTime)
		{
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
					// Only process Variable tick group scripts in this system
					if (script.stateRef >= 0 && !script.disabled && script.tickGroup == LuaTickGroup.Variable)
					{
						m_PendingUpdates.Add(
							(
								entity,
								i,
								script.scriptName.ToString(),
								script.entityIndex,
								script.stateRef,
								script.tickGroup
							)
						);
					}
				}
			}

			foreach (
				var (entity, scriptIndex, scriptName, entityIndex, stateRef, tickGroup) in m_PendingUpdates
			)
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

				m_Vm.CallTick(scriptName, entityIndex, stateRef, deltaTime);
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
