namespace LuaCharacters
{
	using Unity.CharacterController;
	using Unity.Entities;
	using Unity.Mathematics;
	using UnityEngine;

	[DisallowMultipleComponent]
	public class LuaCharacterAuthoring : MonoBehaviour
	{
		public AuthoringKinematicCharacterProperties CharacterProperties =
			AuthoringKinematicCharacterProperties.GetDefault();
		public float RotationSharpness = 25f;
		public float GroundMaxSpeed = 10f;
		public float GroundedMovementSharpness = 15f;
		public float AirAcceleration = 50f;
		public float AirMaxSpeed = 10f;
		public float AirDrag = 0f;
		public float JumpSpeed = 10f;
		public float3 Gravity = math.up() * -30f;
		public bool PreventAirAccelerationAgainstUngroundedHits = true;

		public class Baker : Unity.Entities.Baker<LuaCharacterAuthoring>
		{
			public override void Bake(LuaCharacterAuthoring authoring)
			{
				var entity = GetEntity(TransformUsageFlags.Dynamic);

				// Add kinematic character components
				KinematicCharacterUtilities.BakeCharacter(this, authoring, authoring.CharacterProperties);

				// Add Lua character components
				AddComponent(
					entity,
					new LuaCharacterComponent
					{
						rotationSharpness = authoring.RotationSharpness,
						groundMaxSpeed = authoring.GroundMaxSpeed,
						groundedMovementSharpness = authoring.GroundedMovementSharpness,
						airAcceleration = authoring.AirAcceleration,
						airMaxSpeed = authoring.AirMaxSpeed,
						airDrag = authoring.AirDrag,
						jumpSpeed = authoring.JumpSpeed,
						gravity = authoring.Gravity,
						preventAirAccelerationAgainstUngroundedHits =
							authoring.PreventAirAccelerationAgainstUngroundedHits,
					}
				);

				AddComponent(entity, new LuaCharacterControl { moveInput = float2.zero, jump = false });
			}
		}
	}
}
