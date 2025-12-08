namespace LuaCharacters.ThirdPerson
{
	using System;
	using Unity.CharacterController;
	using Unity.Entities;
	using Unity.Mathematics;
	using static Unity.Mathematics.math;

	[Serializable]
	public struct ThirdPersonCharacterComponent : IComponentData
	{
		public float rotationSharpness;
		public float groundMaxSpeed;
		public float groundedMovementSharpness;
		public float airAcceleration;
		public float airMaxSpeed;
		public float airDrag;
		public float jumpSpeed;
		public float3 gravity;
		public bool preventAirAccelerationAgainstUngroundedHits;
		public BasicStepAndSlopeHandlingParameters stepAndSlopeHandling;
		public bool relativeMovement;
		public bool tankMovement;

		public static ThirdPersonCharacterComponent GetDefault()
		{
			return new ThirdPersonCharacterComponent
			{
				rotationSharpness = 25f,
				groundMaxSpeed = 10f,
				groundedMovementSharpness = 15f,
				airAcceleration = 50f,
				airMaxSpeed = 10f,
				airDrag = 0f,
				jumpSpeed = 10f,
				gravity = up() * -30f,
				preventAirAccelerationAgainstUngroundedHits = true,
				stepAndSlopeHandling = BasicStepAndSlopeHandlingParameters.GetDefault(),
				relativeMovement = false,
				tankMovement = false,
			};
		}
	}

	[Serializable]
	public struct ThirdPersonCharacterControl : IComponentData
	{
		public float3 moveVector;
		public bool jump;
	}
}
