namespace LuaGame.Tests
{
	using System.Collections.Generic;
	using Components;
	using LuaECS.Components;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Transforms;
	using UnityEngine;

	/// <summary>
	/// Visualizer for meteor shower game mode.
	/// Shows victims (agents with health) and damage zones (meteors).
	/// </summary>
	public class MeteorShowerVisualizer : MonoBehaviour
	{
		readonly List<GameObject> m_VictimVisuals = new();
		readonly List<GameObject> m_MeteorVisuals = new();
		Material m_VictimMat;
		Material m_VictimDeadMat;
		Material m_MeteorMat;

		/// <summary>
		/// The world to visualize. If null, uses DefaultGameObjectInjectionWorld.
		/// </summary>
		public World TargetWorld { get; set; }

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

			// Green for living victims
			m_VictimMat = new Material(shader);
			m_VictimMat.color = new Color(0.2f, 0.8f, 0.3f);

			// Gray for dead victims
			m_VictimDeadMat = new Material(shader);
			m_VictimDeadMat.color = new Color(0.3f, 0.3f, 0.3f);

			// Orange/red for meteors
			m_MeteorMat = new Material(shader);
			m_MeteorMat.color = new Color(1f, 0.4f, 0.1f);
		}

		void Update()
		{
			var world = TargetWorld ?? World.DefaultGameObjectInjectionWorld;
			if (world == null || !world.IsCreated)
				return;

			var em = world.EntityManager;
			UpdateVictims(em);
			UpdateMeteors(em);
		}

		void UpdateVictims(EntityManager em)
		{
			using var query = em.CreateEntityQuery(
				ComponentType.ReadOnly<LocalTransform>(),
				ComponentType.ReadOnly<LuaScript>(),
				ComponentType.ReadOnly<LuaHealth>()
			);

			var entities = query.ToEntityArray(Allocator.Temp);
			var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
			var healths = query.ToComponentDataArray<LuaHealth>(Allocator.Temp);

			var victimTransforms = new NativeList<LocalTransform>(entities.Length, Allocator.Temp);
			var victimDead = new NativeList<bool>(entities.Length, Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				var scripts = em.GetBuffer<LuaScript>(entities[i]);
				for (var j = 0; j < scripts.Length; j++)
				{
					if (scripts[j].scriptName.ToString() == "meteor_victim")
					{
						victimTransforms.Add(transforms[i]);
						victimDead.Add(healths[i].isDead);
						break;
					}
				}
			}

			SyncVictimVisuals(victimTransforms, victimDead);

			victimTransforms.Dispose();
			victimDead.Dispose();
			entities.Dispose();
			transforms.Dispose();
			healths.Dispose();
		}

		void UpdateMeteors(EntityManager em)
		{
			using var query = em.CreateEntityQuery(
				ComponentType.ReadOnly<LocalTransform>(),
				ComponentType.ReadOnly<LuaDamageZone>()
			);

			var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
			var damageZones = query.ToComponentDataArray<LuaDamageZone>(Allocator.Temp);

			SyncMeteorVisuals(transforms, damageZones);

			transforms.Dispose();
			damageZones.Dispose();
		}

		void SyncVictimVisuals(NativeList<LocalTransform> transforms, NativeList<bool> dead)
		{
			while (m_VictimVisuals.Count < transforms.Length)
			{
				var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
				go.GetComponent<MeshRenderer>().material = m_VictimMat;
				Destroy(go.GetComponent<Collider>());
				go.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
				m_VictimVisuals.Add(go);
			}

			while (m_VictimVisuals.Count > transforms.Length)
			{
				Destroy(m_VictimVisuals[^1]);
				m_VictimVisuals.RemoveAt(m_VictimVisuals.Count - 1);
			}

			for (var i = 0; i < transforms.Length; i++)
			{
				m_VictimVisuals[i].transform.position = transforms[i].Position;
				m_VictimVisuals[i].GetComponent<MeshRenderer>().material = dead[i]
					? m_VictimDeadMat
					: m_VictimMat;
			}
		}

		void SyncMeteorVisuals(
			NativeArray<LocalTransform> transforms,
			NativeArray<LuaDamageZone> damageZones
		)
		{
			while (m_MeteorVisuals.Count < transforms.Length)
			{
				var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
				go.GetComponent<MeshRenderer>().material = m_MeteorMat;
				Destroy(go.GetComponent<Collider>());
				m_MeteorVisuals.Add(go);
			}

			while (m_MeteorVisuals.Count > transforms.Length)
			{
				Destroy(m_MeteorVisuals[^1]);
				m_MeteorVisuals.RemoveAt(m_MeteorVisuals.Count - 1);
			}

			for (var i = 0; i < transforms.Length; i++)
			{
				m_MeteorVisuals[i].transform.position = transforms[i].Position;
				// Scale sphere to match damage zone radius
				var scale = damageZones[i].radius * 2f;
				m_MeteorVisuals[i].transform.localScale = new Vector3(scale, 0.2f, scale);
			}
		}

		void OnDestroy()
		{
			foreach (var go in m_VictimVisuals)
				if (go != null)
					Destroy(go);
			foreach (var go in m_MeteorVisuals)
				if (go != null)
					Destroy(go);

			if (m_VictimMat != null)
				Destroy(m_VictimMat);
			if (m_VictimDeadMat != null)
				Destroy(m_VictimDeadMat);
			if (m_MeteorMat != null)
				Destroy(m_MeteorMat);
		}
	}
}
