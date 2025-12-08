namespace LuaCharacters.ThirdPerson
{
	using System;
	using Unity.Entities;
	using Unity.Mathematics;
	using UnityEngine.InputSystem;

	/// <summary>
	/// Managed component that stores the InputActionAsset for the player.
	/// Used by the input reading system (non-Burst) to find actions by name.
	/// </summary>
	[Serializable]
	public class ThirdPersonPlayerInputActions : IComponentData
	{
		public InputActionAsset InputActionAsset;
	}

	/// <summary>
	/// Unmanaged component that stores entity references for the player.
	/// Used by Burst-compiled systems that need to access the controlled character and camera.
	/// </summary>
	[Serializable]
	public struct ThirdPersonPlayerControl : IComponentData
	{
		public Entity controlledCharacter;
		public Entity controlledCamera;
	}

	[Serializable]
	public struct ThirdPersonPlayerInputs : IComponentData
	{
		public float2 moveInput;
		public float2 cameraLookInput;
		public float cameraZoomInput;
		public FixedInputEvent jumpPressed;
	}
}
