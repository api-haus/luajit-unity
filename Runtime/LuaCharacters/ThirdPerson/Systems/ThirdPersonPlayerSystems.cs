#if UNITY_NETCODE
using Unity.NetCode;
#endif

namespace LuaCharacters.ThirdPerson.Systems
{
	using Components;
	using Unity.Burst;
	using Unity.CharacterController;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Transforms;
	using UnityEngine;
	using UnityEngine.InputSystem;
	using static Unity.Mathematics.math;
	using quaternion = Unity.Mathematics.quaternion;

	[UpdateInGroup(typeof(InitializationSystemGroup))]
	public partial class ThirdPersonPlayerInputsSystem : SystemBase
	{
		private InputAction m_MoveAction;
		private InputAction m_JumpAction;

		protected override void OnCreate()
		{
			RequireForUpdate<FixedTickSystem.Singleton>();
			RequireForUpdate(
				SystemAPI
					.QueryBuilder()
					.WithAll<ThirdPersonPlayerInputActions, ThirdPersonPlayerInputs>()
					.Build()
			);
		}

		protected override void OnStartRunning()
		{
			// Get the InputActionAsset and find actions by convention names
			var inputActions = SystemAPI.ManagedAPI.GetSingleton<ThirdPersonPlayerInputActions>();

			if (inputActions.inputActionAsset != null)
			{
				m_MoveAction = inputActions.inputActionAsset.FindAction("Move");
				m_JumpAction = inputActions.inputActionAsset.FindAction("Jump");

				// Enable the actions
				if (m_MoveAction != null)
				{
					m_MoveAction.Enable();
				}
				if (m_JumpAction != null)
				{
					m_JumpAction.Enable();
				}
			}
		}

		protected override void OnStopRunning()
		{
			// Disable actions when the system stops
			if (m_MoveAction != null)
			{
				m_MoveAction.Disable();
				m_MoveAction = null;
			}
			if (m_JumpAction != null)
			{
				m_JumpAction.Disable();
				m_JumpAction = null;
			}
		}

		protected override void OnUpdate()
		{
			var fixedTick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().tick;

			foreach (var playerInputs in SystemAPI.Query<RefRW<ThirdPersonPlayerInputs>>())
			{
				// Read move input from the Move action
				if (m_MoveAction != null && m_MoveAction.enabled)
				{
					var moveValue = m_MoveAction.ReadValue<Vector2>();
					playerInputs.ValueRW.moveInput = new float2(moveValue.x, moveValue.y);
				}
				else
				{
					playerInputs.ValueRW.moveInput = new float2(0f, 0f);
				}

				// Read jump input from the Jump action
				if (m_JumpAction != null && m_JumpAction.enabled)
				{
					if (m_JumpAction.WasPressedThisFrame())
					{
						playerInputs.ValueRW.jumpPressed.Set(fixedTick);
					}
				}
			}
		}
	}

	/// <summary>
	/// Apply inputs that need to be read at a fixed rate.
	/// It is necessary to handle this as part of the fixed step group, in case your framerate is lower than the fixed step rate.
	/// </summary>
	[UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
	[BurstCompile]
	public partial struct ThirdPersonPlayerFixedStepControlSystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<FixedTickSystem.Singleton>();
			state.RequireForUpdate(
				SystemAPI
					.QueryBuilder()
					.WithAll<ThirdPersonPlayerControl, ThirdPersonPlayerInputs>()
					.Build()
			);
		}

		public void OnDestroy(ref SystemState state) { }

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var fixedTick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().tick;

			foreach (
				var (playerInputs, player) in SystemAPI
					.Query<RefRW<ThirdPersonPlayerInputs>, ThirdPersonPlayerControl>()
					.WithAll<Simulate>()
			)
			{
				if (SystemAPI.HasComponent<ThirdPersonCharacterControl>(player.controlledCharacter))
				{
					var characterControl = SystemAPI.GetComponent<ThirdPersonCharacterControl>(
						player.controlledCharacter
					);
					var charOptions = SystemAPI.GetComponent<ThirdPersonCharacterComponent>(
						player.controlledCharacter
					);

					ref var characterRotation = ref SystemAPI
						.GetComponentRW<LocalTransform>(player.controlledCharacter)
						.ValueRW;
					var characterUp = MathUtilities.GetUpFromRotation(characterRotation.Rotation);

					if (charOptions.tankMovement)
					{
						var forwardOnUpPlane = normalizesafe(
							MathUtilities.ProjectOnPlane(
								MathUtilities.GetForwardFromRotation(characterRotation.Rotation),
								characterUp
							)
						);

						// Move
						characterControl.moveVector = (playerInputs.ValueRW.moveInput.y * forwardOnUpPlane);
						characterControl.moveVector = MathUtilities.ClampToMaxLength(
							characterControl.moveVector,
							1f
						);

						var angle = radians(playerInputs.ValueRW.moveInput.x * 1);
						characterRotation = characterRotation.RotateY(angle);
					}
					else
					{
						// Get camera rotation data, since our movement is relative to it
						var cameraRotation = quaternion.identity;

#if UNITY_NETCODE
						if (SystemAPI.HasComponent<TPC_NetcodeInput>(player.ControlledCamera))
						{
							var tpcNetcodeInput = SystemAPI.GetComponent<TPC_NetcodeInput>(
								player.ControlledCamera
							);
							var eulerAngles = new float3(
								tpcNetcodeInput.CameraAngles.x,
								tpcNetcodeInput.CameraAngles.y,
								0
							);

							cameraRotation = mul(
								quaternion.LookRotationSafe(tpcNetcodeInput.PlanarForward, characterUp),
								quaternion.Euler(eulerAngles)
							);
						}
#else
						if (SystemAPI.HasComponent<LocalTransform>(player.controlledCamera))
						{
							cameraRotation = SystemAPI
								.GetComponent<LocalTransform>(player.controlledCamera)
								.Rotation;
						}
#endif

						var cameraForwardOnUpPlane = normalizesafe(
							MathUtilities.ProjectOnPlane(
								MathUtilities.GetForwardFromRotation(cameraRotation),
								characterUp
							)
						);
						var cameraRight = MathUtilities.GetRightFromRotation(cameraRotation);

						// Move
						characterControl.moveVector =
							(playerInputs.ValueRW.moveInput.y * cameraForwardOnUpPlane)
							+ (playerInputs.ValueRW.moveInput.x * cameraRight);
						characterControl.moveVector = MathUtilities.ClampToMaxLength(
							characterControl.moveVector,
							1f
						);

						// Jump
						// We use the "FixedInputEvent" helper struct here to detect if the event needs to be processed.
						// This is part of a strategy for proper handling of button press events that are consumed during the fixed update group.
						characterControl.jump = playerInputs.ValueRW.jumpPressed.IsSet(fixedTick);
					}

					SystemAPI.SetComponent(player.controlledCharacter, characterControl);
				}
			}
		}
	}
}
