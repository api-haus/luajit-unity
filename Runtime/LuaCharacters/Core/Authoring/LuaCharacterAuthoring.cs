namespace LuaCharacters.Core.Authoring
{
	using Unity.CharacterController;
	using Unity.Entities;
	using Unity.Mathematics;
	using UnityEngine;

	[DisallowMultipleComponent]
	public class LuaCharacterAuthoring : MonoBehaviour
	{
		public AuthoringKinematicCharacterProperties characterProperties =
			AuthoringKinematicCharacterProperties.GetDefault();
		public float rotationSharpness = 25f;
		public float groundMaxSpeed = 10f;
		public float groundedMovementSharpness = 15f;
		public float airAcceleration = 50f;
		public float airMaxSpeed = 10f;
		public float airDrag = 0f;
		public float jumpSpeed = 10f;
		public float3 gravity = math.up() * -30f;
		public bool preventAirAccelerationAgainstUngroundedHits = true;

		public class Baker : Baker<LuaCharacterAuthoring>
		{
			public override void Bake(LuaCharacterAuthoring authoring)
			{
				var entity = GetEntity(TransformUsageFlags.Dynamic);

				// Add kinematic character components
				KinematicCharacterUtilities.BakeCharacter(this, authoring, authoring.characterProperties);

				// Add Lua character components
				AddComponent(
					entity,
					new LuaCharacterComponent
					{
						rotationSharpness = authoring.rotationSharpness,
						groundMaxSpeed = authoring.groundMaxSpeed,
						groundedMovementSharpness = authoring.groundedMovementSharpness,
						airAcceleration = authoring.airAcceleration,
						airMaxSpeed = authoring.airMaxSpeed,
						airDrag = authoring.airDrag,
						jumpSpeed = authoring.jumpSpeed,
						gravity = authoring.gravity,
						preventAirAccelerationAgainstUngroundedHits =
							authoring.preventAirAccelerationAgainstUngroundedHits,
					}
				);

				AddComponent(entity, new LuaCharacterControl { moveInput = float2.zero, jump = false });
			}
		}
	}
}
