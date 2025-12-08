namespace LuaCharacters
{
	using System;
	using Unity.Entities;
	using Unity.Mathematics;

	[Serializable]
	public struct LuaCharacterComponent : IComponentData
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

		public static LuaCharacterComponent GetDefault()
		{
			return new LuaCharacterComponent
			{
				rotationSharpness = 25f,
				groundMaxSpeed = 10f,
				groundedMovementSharpness = 15f,
				airAcceleration = 50f,
				airMaxSpeed = 10f,
				airDrag = 0f,
				jumpSpeed = 10f,
				gravity = math.up() * -30f,
				preventAirAccelerationAgainstUngroundedHits = true,
			};
		}
	}

	[Serializable]
	public struct LuaCharacterControl : IComponentData
	{
		public float2 moveInput;
		public bool jump;
	}
}
