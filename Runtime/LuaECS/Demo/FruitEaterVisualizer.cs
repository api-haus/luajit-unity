namespace LuaECS.Tests
{
	using System.Collections.Generic;
	using Components;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Transforms;
	using UnityEngine;

	public class FruitEaterVisualizer : MonoBehaviour
	{
		readonly List<GameObject> m_AgentVisuals = new();
		readonly List<GameObject> m_FruitVisuals = new();
		Material m_AgentMat;
		Material m_FruitMat;

		/// <summary>
		/// The world to visualize. If null, uses DefaultGameObjectInjectionWorld.
		/// </summary>
		public World TargetWorld { get; set; }

		/// <summary>
		/// When true, queries by script name instead of tag components.
		/// Use this for entities created via Lua that don't have AgentTag/FruitTag.
		/// </summary>
		public bool QueryByScriptName { get; set; }

		void Start()
		{
			var shader = Shader.Find("Universal Render Pipeline/Lit");
			if (shader == null)
				shader = Shader.Find("Standard");
			if (shader == null)
				shader = Shader.Find("Sprites/Default");

			if (shader == null)
			{
				enabled = false;
				return;
			}

			m_AgentMat = new Material(shader);
			m_AgentMat.color = new Color(0.2f, 0.5f, 1f);

			m_FruitMat = new Material(shader);
			m_FruitMat.color = new Color(1f, 0.3f, 0.2f);
		}

		void Update()
		{
			var world = TargetWorld ?? World.DefaultGameObjectInjectionWorld;
			if (world == null || !world.IsCreated)
				return;

			var em = world.EntityManager;

			if (QueryByScriptName)
			{
				UpdateByScriptName(em);
			}
			else
			{
				UpdateByTag(em);
			}
		}

		void UpdateByTag(EntityManager em)
		{
			var agentQuery = em.CreateEntityQuery(
				ComponentType.ReadOnly<LocalTransform>(),
				ComponentType.ReadOnly<AgentTag>()
			);

			var fruitQuery = em.CreateEntityQuery(
				ComponentType.ReadOnly<LocalTransform>(),
				ComponentType.ReadOnly<FruitTag>()
			);

			var agentTransforms = agentQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
			var fruitTransforms = fruitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

			SyncVisuals(m_AgentVisuals, agentTransforms, m_AgentMat, 0.5f);
			SyncVisuals(m_FruitVisuals, fruitTransforms, m_FruitMat, 0.35f);

			agentTransforms.Dispose();
			fruitTransforms.Dispose();
		}

		void UpdateByScriptName(EntityManager em)
		{
			using var scriptQuery = em.CreateEntityQuery(
				ComponentType.ReadOnly<LocalTransform>(),
				ComponentType.ReadOnly<LuaScript>()
			);

			var entities = scriptQuery.ToEntityArray(Allocator.Temp);
			var transforms = scriptQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

			var agentTransforms = new NativeList<LocalTransform>(entities.Length, Allocator.Temp);
			var fruitTransforms = new NativeList<LocalTransform>(entities.Length, Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				var scripts = em.GetBuffer<LuaScript>(entities[i]);
				for (var j = 0; j < scripts.Length; j++)
				{
					var scriptName = scripts[j].scriptName.ToString();
					if (scriptName == "fruit_eater")
					{
						agentTransforms.Add(transforms[i]);
						break;
					}
					if (scriptName == "fruit")
					{
						fruitTransforms.Add(transforms[i]);
						break;
					}
				}
			}

			SyncVisuals(m_AgentVisuals, agentTransforms.AsArray(), m_AgentMat, 0.5f);
			SyncVisuals(m_FruitVisuals, fruitTransforms.AsArray(), m_FruitMat, 0.35f);

			agentTransforms.Dispose();
			fruitTransforms.Dispose();
			entities.Dispose();
			transforms.Dispose();
		}

		void SyncVisuals(
			List<GameObject> visuals,
			NativeArray<LocalTransform> transforms,
			Material mat,
			float scale
		)
		{
			while (visuals.Count < transforms.Length)
			{
				var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
				go.GetComponent<MeshRenderer>().material = mat;
				Destroy(go.GetComponent<Collider>());
				go.transform.localScale = Vector3.one * scale;
				visuals.Add(go);
			}

			while (visuals.Count > transforms.Length)
			{
				Destroy(visuals[^1]);
				visuals.RemoveAt(visuals.Count - 1);
			}

			for (var i = 0; i < transforms.Length; i++)
			{
				visuals[i].transform.position = transforms[i].Position;
			}
		}

		void OnDestroy()
		{
			foreach (var go in m_AgentVisuals)
				if (go != null)
					Destroy(go);
			foreach (var go in m_FruitVisuals)
				if (go != null)
					Destroy(go);

			if (m_AgentMat != null)
				Destroy(m_AgentMat);
			if (m_FruitMat != null)
				Destroy(m_FruitMat);
		}
	}
}
