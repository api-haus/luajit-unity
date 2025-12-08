namespace LuaECS.Systems
{
	using System.Collections.Generic;
	using LuaCharacters;
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaECS.Systems.Support;
	using LuaVM.Core;
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
		LuaVMManager m_VM;
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

		static int s_FrameCount;

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
			if (m_VM == null)
			{
				m_VM = LuaVMManager.GetOrCreate();

				m_VM.RegisterBridgeNow(LuaECSBridge.RegisterFunctions);
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
			var creations = LuaECSBridge.FlushCreations();
			for (var i = 0; i < creations.Length; i++)
			{
				var creation = creations[i];
				LuaEntityRegistry.CreateWithId(creation.EntityId, creation.Position, m_CurrentECB);
			}
			creations.Dispose();

			var scripts = LuaECSBridge.FlushScriptAdditions();
			for (var i = 0; i < scripts.Length; i++)
			{
				var script = scripts[i];
				LuaEntityRegistry.AddScriptDeferred(
					script.EntityId,
					script.ScriptName.ToString(),
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
			CollectPendingEvents();
			ClearEventBuffers();
			DispatchCollectedEvents();
		}

		void CollectPendingEvents()
		{
			LuaECSBridge.ClearEventContext();
			ref var ctx = ref LuaECSBridge.EventContext;
			if (!ctx.IsValid)
				return;

			var entities = m_EventQuery.ToEntityArray(Allocator.Temp);
			foreach (var entity in entities)
			{
				var events = EntityManager.GetBuffer<LuaEvent>(entity);
				if (events.Length == 0)
					continue;

				LuaECSBridge.AddEntityToClear(entity);

				var eventStartIndex = ctx.EventBuffer.Length;
				for (var i = 0; i < events.Length; i++)
				{
					LuaECSBridge.AddEvent(events[i]);
				}
				var eventCount = events.Length;

				var scripts = EntityManager.GetBuffer<LuaScript>(entity);
				for (var i = 0; i < scripts.Length; i++)
				{
					var script = scripts[i];
					if (script.StateRef >= 0 && !script.Disabled)
					{
						LuaECSBridge.AddEventDispatch(
							entity,
							i,
							script.ScriptName,
							script.EntityIndex,
							script.StateRef,
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
			if (!ctx.IsValid)
				return;

			for (var i = 0; i < ctx.EntitiesToClear.Length; i++)
			{
				m_CurrentECB.SetBuffer<LuaEvent>(ctx.EntitiesToClear[i]);
			}
		}

		void DispatchCollectedEvents()
		{
			ref var ctx = ref LuaECSBridge.EventContext;
			if (!ctx.IsValid)
				return;

			for (var i = 0; i < ctx.PendingEvents.Length; i++)
			{
				var dispatch = ctx.PendingEvents[i];

				if (!EntityManager.Exists(dispatch.Entity))
					continue;

				if (!EntityManager.HasComponent<LuaEntityId>(dispatch.Entity))
					continue;

				var scriptName = dispatch.ScriptName.ToString();

				for (var j = 0; j < dispatch.EventCount; j++)
				{
					var evt = LuaECSBridge.GetEvent(dispatch.EventStartIndex + j);
					var eventName = evt.EventName.ToString();
					var sourceId = LuaEntityRegistry.GetEntityIdFromEntity(evt.Source, EntityManager);
					var targetId = LuaEntityRegistry.GetEntityIdFromEntity(evt.Target, EntityManager);

					m_VM.CallEvent(
						scriptName,
						dispatch.EntityIndex,
						dispatch.StateRef,
						eventName,
						sourceId,
						targetId,
						evt.IntParam
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
				if (script.StateRef < 0 || script.Disabled)
					continue;

				var scriptName = script.ScriptName.ToString();
				m_VM.CallCommand(scriptName, script.EntityIndex, script.StateRef, command);
			}
		}

		#endregion
	}
}
