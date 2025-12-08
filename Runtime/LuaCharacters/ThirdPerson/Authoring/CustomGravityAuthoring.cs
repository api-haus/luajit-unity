namespace LuaCharacters.ThirdPerson
{
	using System;
	using Unity.Entities;
	using Unity.Mathematics;
	using UnityEngine;

	[Serializable]
	public struct CustomGravity : IComponentData
	{
		public float gravityMultiplier;

		[HideInInspector]
		public float3 gravity;

		[HideInInspector]
		public bool touchedByNonGlobalGravity;

		public Entity currentZoneEntity;

		public Entity lastZoneEntity;
	}

	[DisallowMultipleComponent]
	public class CustomGravityAuthoring : MonoBehaviour
	{
		public float gravityMultiplier = 1f;

		class Baker : Unity.Entities.Baker<CustomGravityAuthoring>
		{
			public override void Bake(CustomGravityAuthoring authoring)
			{
				var entity = GetEntity(TransformUsageFlags.Dynamic);
				AddComponent(entity, new CustomGravity { gravityMultiplier = authoring.gravityMultiplier });
			}
		}
	}
}
