namespace LuaECS.Authoring
{
	using System.Collections.Generic;
	using Components;
	using Core;
	using Unity.Collections;
	using Unity.Entities;
	using UnityEngine;

	[DisallowMultipleComponent]
	[ExecuteAlways]
	public sealed class LuaScriptBufferAuthoring : MonoBehaviour
	{
		void OnEnable()
		{
			hideFlags |= HideFlags.NotEditable;
		}

		void OnValidate()
		{
			hideFlags |= HideFlags.NotEditable;
		}

		public class Baker : Baker<LuaScriptBufferAuthoring>
		{
			readonly List<LuaScriptAuthoring> m_Scripts = new();

			public override void Bake(LuaScriptBufferAuthoring authoring)
			{
				authoring.GetComponents(m_Scripts);

				var hasValidScript = false;

				foreach (var script in m_Scripts)
				{
					DependsOn(script);

					if (!hasValidScript && script != null && script.script.IsValid)
						hasValidScript = true;
				}

				if (!hasValidScript)
				{
					m_Scripts.Clear();
					return;
				}

				var entity = GetEntity(TransformUsageFlags.None);
				var requestsBuffer = AddBuffer<LuaScriptRequest>(entity);

				foreach (var scriptAuthor in m_Scripts)
				{
					if (scriptAuthor == null || !scriptAuthor.script.IsValid)
						continue;

					var scriptId = LuaScriptPathUtility.NormalizeScriptId(scriptAuthor.script.ToString());
					if (string.IsNullOrEmpty(scriptId))
						continue;

					var hash = LuaScriptPathUtility.HashScriptName(scriptId);

					requestsBuffer.Add(
						new LuaScriptRequest
						{
							scriptName = new FixedString64Bytes(scriptId),
							requestHash = hash,
							fulfilled = false,
						}
					);
				}

				AddBuffer<LuaEvent>(entity);

				// Add LuaEntityId with sentinel value 0. The fulfillment system will assign
				// a real ID (> 0) when the script is first initialized.
				AddComponent(entity, new LuaEntityId { value = 0 });

				m_Scripts.Clear();
			}
		}
	}
}
