namespace LuaECS.Systems.Support
{
	using System.Collections.Generic;
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaVM.Core;
	using Unity.Collections;
	using Unity.Entities;

	/// <summary>
	/// Handles event collection and dispatch to Lua scripts.
	/// Separates event handling from the main system.
	/// </summary>
	public class LuaEventDispatcher
	{
		readonly List<(
			Entity entity,
			int scriptIndex,
			string scriptName,
			int entityIndex,
			int stateRef,
			List<LuaEvent> events
		)> m_PendingEvents;

		readonly List<Entity> m_EntitiesToClear;

		LuaVMManager m_VM;
		EntityManager m_EntityManager;
		EntityQuery m_EventQuery;

		public LuaEventDispatcher(LuaVMManager vm, EntityManager entityManager, EntityQuery eventQuery)
		{
			m_VM = vm;
			m_EntityManager = entityManager;
			m_EventQuery = eventQuery;
			m_PendingEvents = new List<(Entity, int, string, int, int, List<LuaEvent>)>(64);
			m_EntitiesToClear = new List<Entity>(64);
		}

		public void SetVM(LuaVMManager vm)
		{
			m_VM = vm;
		}

		/// <summary>
		/// Collects events from all entities with LuaScript and LuaEvent buffers.
		/// Returns count of events collected.
		/// </summary>
		public int CollectPendingEvents()
		{
			m_PendingEvents.Clear();
			m_EntitiesToClear.Clear();
			var eventCount = 0;

			var entities = m_EventQuery.ToEntityArray(Allocator.Temp);
			foreach (var entity in entities)
			{
				var events = m_EntityManager.GetBuffer<LuaEvent>(entity);
				if (events.Length == 0)
					continue;

				eventCount += events.Length;
				m_EntitiesToClear.Add(entity);

				var eventsCopy = new List<LuaEvent>(events.Length);
				for (var i = 0; i < events.Length; i++)
				{
					eventsCopy.Add(events[i]);
				}

				var scripts = m_EntityManager.GetBuffer<LuaScript>(entity);
				for (var i = 0; i < scripts.Length; i++)
				{
					var script = scripts[i];
					if (script.StateRef >= 0 && !script.Disabled)
					{
						m_PendingEvents.Add(
							(
								entity,
								i,
								script.ScriptName.ToString(),
								script.EntityIndex,
								script.StateRef,
								eventsCopy
							)
						);
					}
				}
			}
			entities.Dispose();

			return eventCount;
		}

		/// <summary>
		/// Clears event buffers on entities that had events.
		/// </summary>
		public void ClearEventBuffers(EntityCommandBuffer ecb)
		{
			foreach (var entity in m_EntitiesToClear)
			{
				ecb.SetBuffer<LuaEvent>(entity);
			}
		}

		/// <summary>
		/// Dispatches collected events to Lua scripts via CallEvent.
		/// </summary>
		public void DispatchEvents()
		{
			foreach (
				var (entity, scriptIndex, scriptName, entityIndex, stateRef, events) in m_PendingEvents
			)
			{
				if (!m_EntityManager.Exists(entity))
					continue;

				if (!m_EntityManager.HasComponent<LuaEntityId>(entity))
					continue;

				foreach (var evt in events)
				{
					var eventName = evt.EventName.ToString();
					var sourceId = LuaEntityRegistry.GetEntityIdFromEntity(evt.Source, m_EntityManager);
					var targetId = LuaEntityRegistry.GetEntityIdFromEntity(evt.Target, m_EntityManager);

					m_VM.CallEvent(
						scriptName,
						entityIndex,
						stateRef,
						eventName,
						sourceId,
						targetId,
						evt.IntParam
					);
				}
			}
		}
	}
}
