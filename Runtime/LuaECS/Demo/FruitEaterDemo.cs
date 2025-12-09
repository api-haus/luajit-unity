namespace LuaECS.Demo
{
	using Components;
	using Core;
	using LuaVM.Core;
	using Systems;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Transforms;
	using UnityEngine;

	/// <summary>
	/// Minimal C# bootstrapper - all game logic is in Lua.
	/// Creates a single bootstrap entity that runs fruit_demo_bootstrap.lua
	/// </summary>
	public class FruitEaterDemo : MonoBehaviour
	{
		[Header("Visualization")]
		public Color agentColor = Color.blue;
		public Color fruitColor = Color.red;
		public float agentSize = 0.4f;
		public float fruitSize = 0.25f;
		public float gridSize = 10f;

		World m_World;
		EntityManager m_EntityManager;
		Entity m_BootstrapEntity;
		bool m_Initialized;

		EntityQuery m_ScriptedEntitiesQuery;

		void Start()
		{
			m_World = World.DefaultGameObjectInjectionWorld;
			m_EntityManager = m_World.EntityManager;

			LuaVMManager.GetOrCreate();

			var scriptingSystem = m_World.GetExistingSystemManaged<LuaScriptingSystem>();
			if (scriptingSystem != null)
			{
				LuaECSBridge.Initialize(m_World, scriptingSystem);
			}

			m_ScriptedEntitiesQuery = m_EntityManager.CreateEntityQuery(
				ComponentType.ReadOnly<LocalTransform>(),
				ComponentType.ReadOnly<LuaScript>()
			);

			m_BootstrapEntity = m_EntityManager.CreateEntity(typeof(LocalTransform));
			var requests = m_EntityManager.AddBuffer<LuaScriptRequest>(m_BootstrapEntity);
			const string bootstrapScript = "fruit_demo_bootstrap";
			requests.Add(
				new LuaScriptRequest
				{
					scriptName = bootstrapScript,
					requestHash = LuaScriptPathUtility.HashScriptName(bootstrapScript),
					fulfilled = false,
				}
			);
			m_EntityManager.AddBuffer<LuaEvent>(m_BootstrapEntity);
			m_EntityManager.AddComponentData(m_BootstrapEntity, new LuaEntityId { value = 0 });

			m_Initialized = true;
			Log.Info("[FruitDemo] Bootstrap entity created - Lua takes over from here");
		}

		void Update()
		{
			if (!m_Initialized)
				return;

			DrawDebugVisualization();
		}

		void DrawDebugVisualization()
		{
			var transforms = m_ScriptedEntitiesQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
			var entities = m_ScriptedEntitiesQuery.ToEntityArray(Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				var pos = (Vector3)transforms[i].Position;

				if (entities[i] == m_BootstrapEntity)
					continue;

				var scripts = m_EntityManager.GetBuffer<LuaScript>(entities[i]);
				var isAgent = false;
				for (var j = 0; j < scripts.Length; j++)
				{
					if (scripts[j].scriptName.ToString().Contains("eater"))
					{
						isAgent = true;
						break;
					}
				}

				if (isAgent)
				{
					Debug.DrawLine(
						pos + (Vector3.left * agentSize),
						pos + (Vector3.right * agentSize),
						agentColor
					);
					Debug.DrawLine(
						pos + (Vector3.forward * agentSize),
						pos + (Vector3.back * agentSize),
						agentColor
					);
					Debug.DrawLine(
						pos + (Vector3.up * agentSize),
						pos + (Vector3.down * agentSize),
						agentColor
					);
				}
				else
				{
					var s = fruitSize;
					Debug.DrawLine(pos + new Vector3(-s, 0, -s), pos + new Vector3(s, 0, s), fruitColor);
					Debug.DrawLine(pos + new Vector3(-s, 0, s), pos + new Vector3(s, 0, -s), fruitColor);
				}
			}

			transforms.Dispose();
			entities.Dispose();

			var half = gridSize / 2f;
			Debug.DrawLine(new Vector3(-half, 0, -half), new Vector3(half, 0, -half), Color.gray);
			Debug.DrawLine(new Vector3(half, 0, -half), new Vector3(half, 0, half), Color.gray);
			Debug.DrawLine(new Vector3(half, 0, half), new Vector3(-half, 0, half), Color.gray);
			Debug.DrawLine(new Vector3(-half, 0, half), new Vector3(-half, 0, -half), Color.gray);
		}

		void OnDestroy()
		{
			if (m_Initialized && m_EntityManager != null)
			{
				m_EntityManager.DestroyEntity(m_ScriptedEntitiesQuery);
			}
		}

		void OnDrawGizmos()
		{
			if (!Application.isPlaying || !m_Initialized)
				return;

			Gizmos.color = Color.gray;
			Gizmos.DrawWireCube(Vector3.zero, new Vector3(gridSize, 0.1f, gridSize));

			var transforms = m_ScriptedEntitiesQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
			var entities = m_ScriptedEntitiesQuery.ToEntityArray(Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				if (entities[i] == m_BootstrapEntity)
					continue;

				var scripts = m_EntityManager.GetBuffer<LuaScript>(entities[i]);
				var isAgent = false;
				for (var j = 0; j < scripts.Length; j++)
				{
					if (scripts[j].scriptName.ToString().Contains("eater"))
					{
						isAgent = true;
						break;
					}
				}

				Gizmos.color = isAgent ? agentColor : fruitColor;
				Gizmos.DrawSphere(transforms[i].Position, isAgent ? agentSize : fruitSize);
			}

			transforms.Dispose();
			entities.Dispose();
		}

		void OnGUI()
		{
			if (!m_Initialized)
				return;

			var entityCount = m_ScriptedEntitiesQuery.CalculateEntityCount() - 1;

			GUILayout.BeginArea(new Rect(10, 10, 200, 100));
			GUILayout.Label($"Scripted Entities: {entityCount}");
			GUILayout.Label("Bootstrap: Lua-driven");
			GUILayout.EndArea();
		}
	}
}
