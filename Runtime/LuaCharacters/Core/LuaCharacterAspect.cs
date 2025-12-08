namespace LuaCharacters.Core
{
	using Unity.Burst;
	using Unity.CharacterController;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Physics;
	using static Unity.Mathematics.math;

	public struct LuaCharacterUpdateContext
	{
		// Global data accessible to all character updates
	}

	[BurstCompile]
	public readonly partial struct LuaCharacterAspect
		: IAspect,
			IKinematicCharacterProcessor<LuaCharacterUpdateContext>
	{
		public readonly KinematicCharacterAspect characterAspect;
		public readonly RefRW<LuaCharacterComponent> character;
		public readonly RefRW<LuaCharacterControl> control;

		public void PhysicsUpdate(
			ref LuaCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext
		)
		{
			ref var characterComponent = ref character.ValueRW;
			ref var characterBody = ref characterAspect.CharacterBody.ValueRW;
			ref var characterPosition = ref characterAspect.LocalTransform.ValueRW.Position;
			var stepHandling = BasicStepAndSlopeHandlingParameters.GetDefault();

			characterAspect.Update_Initialize(
				in this,
				ref context,
				ref baseContext,
				ref characterBody,
				baseContext.Time.DeltaTime
			);
			characterAspect.Update_ParentMovement(
				in this,
				ref context,
				ref baseContext,
				ref characterBody,
				ref characterPosition,
				characterBody.WasGroundedBeforeCharacterUpdate
			);
			characterAspect.Update_Grounding(
				in this,
				ref context,
				ref baseContext,
				ref characterBody,
				ref characterPosition
			);

			HandleVelocityControl(ref context, ref baseContext);

			characterAspect.Update_PreventGroundingFromFutureSlopeChange(
				in this,
				ref context,
				ref baseContext,
				ref characterBody,
				in stepHandling
			);
			characterAspect.Update_GroundPushing(
				in this,
				ref context,
				ref baseContext,
				characterComponent.gravity
			);
			characterAspect.Update_MovementAndDecollisions(
				in this,
				ref context,
				ref baseContext,
				ref characterBody,
				ref characterPosition
			);
			characterAspect.Update_MovingPlatformDetection(ref baseContext, ref characterBody);
			characterAspect.Update_ParentMomentum(ref baseContext, ref characterBody);
			characterAspect.Update_ProcessStatefulCharacterHits();
		}

		void HandleVelocityControl(
			ref LuaCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext
		)
		{
			var deltaTime = baseContext.Time.DeltaTime;
			ref var characterBody = ref characterAspect.CharacterBody.ValueRW;
			ref var characterComponent = ref character.ValueRW;
			ref var controlComponent = ref control.ValueRW;

			// Rotate move input to account for parent rotation
			if (characterBody.ParentEntity != Entity.Null)
			{
				var moveVector3 = new float3(controlComponent.moveInput.x, 0, controlComponent.moveInput.y);
				moveVector3 = rotate(characterBody.RotationFromParent, moveVector3);
				controlComponent.moveInput = new float2(moveVector3.x, moveVector3.z);
				characterBody.RelativeVelocity = rotate(
					characterBody.RotationFromParent,
					characterBody.RelativeVelocity
				);
			}

			if (characterBody.IsGrounded)
			{
				// Movement
				var characterRotation = characterAspect.LocalTransform.ValueRO.Rotation;
				var targetVelocity =
					(controlComponent.moveInput.y * MathUtilities.GetForwardFromRotation(characterRotation))
					+ (controlComponent.moveInput.x * MathUtilities.GetRightFromRotation(characterRotation));
				targetVelocity = MathUtilities.ClampToMaxLength(targetVelocity, 1f);
				targetVelocity *= characterComponent.groundMaxSpeed;
				CharacterControlUtilities.StandardGroundMove_Interpolated(
					ref characterBody.RelativeVelocity,
					targetVelocity,
					characterComponent.groundedMovementSharpness,
					deltaTime,
					characterBody.GroundingUp,
					characterBody.GroundHit.Normal
				);

				// Jump
				if (controlComponent.jump)
				{
					CharacterControlUtilities.StandardJump(
						ref characterBody,
						characterBody.GroundingUp * characterComponent.jumpSpeed,
						true,
						characterBody.GroundingUp
					);
				}
			}
			else
			{
				// Air movement
				var characterRotation = characterAspect.LocalTransform.ValueRO.Rotation;
				var airAcceleration =
					(controlComponent.moveInput.y * MathUtilities.GetForwardFromRotation(characterRotation))
					+ (controlComponent.moveInput.x * MathUtilities.GetRightFromRotation(characterRotation));
				airAcceleration = MathUtilities.ClampToMaxLength(airAcceleration, 1f);
				airAcceleration *= characterComponent.airAcceleration;
				CharacterControlUtilities.StandardAirMove(
					ref characterBody.RelativeVelocity,
					airAcceleration,
					characterComponent.airMaxSpeed,
					characterBody.GroundingUp,
					deltaTime,
					false
				);

				// Gravity
				CharacterControlUtilities.AccelerateVelocity(
					ref characterBody.RelativeVelocity,
					characterComponent.gravity,
					deltaTime
				);

				// Drag
				CharacterControlUtilities.ApplyDragToVelocity(
					ref characterBody.RelativeVelocity,
					deltaTime,
					characterComponent.airDrag
				);
			}
		}

		public void VariableUpdate(
			ref LuaCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext
		)
		{
			ref var characterBody = ref characterAspect.CharacterBody.ValueRW;
			ref var characterRotation = ref characterAspect.LocalTransform.ValueRW.Rotation;
			ref var characterComponent = ref character.ValueRW;
			ref var controlComponent = ref control.ValueRW;

			// Add rotation from parent body
			KinematicCharacterUtilities.AddVariableRateRotationFromFixedRateRotation(
				ref characterRotation,
				characterBody.RotationFromParent,
				baseContext.Time.DeltaTime,
				characterBody.LastPhysicsUpdateDeltaTime
			);

			// Rotate towards movement direction
			if (lengthsq(controlComponent.moveInput) > 0f)
			{
				var moveDirection =
					(controlComponent.moveInput.y * MathUtilities.GetForwardFromRotation(characterRotation))
					+ (controlComponent.moveInput.x * MathUtilities.GetRightFromRotation(characterRotation));

				CharacterControlUtilities.SlerpRotationTowardsDirectionAroundUp(
					ref characterRotation,
					baseContext.Time.DeltaTime,
					normalizesafe(moveDirection),
					MathUtilities.GetUpFromRotation(characterRotation),
					characterComponent.rotationSharpness
				);
			}
		}

		public void UpdateGroundingUp(
			ref LuaCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext
		)
		{
			ref var characterBody = ref characterAspect.CharacterBody.ValueRW;
			characterAspect.Default_UpdateGroundingUp(ref characterBody);
		}

		public bool CanCollideWithHit(
			ref LuaCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext,
			in BasicHit hit
		)
		{
			return PhysicsUtilities.IsCollidable(hit.Material);
		}

		public bool IsGroundedOnHit(
			ref LuaCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext,
			in BasicHit hit,
			int groundingEvaluationType
		)
		{
			var stepHandling = BasicStepAndSlopeHandlingParameters.GetDefault();
			return characterAspect.Default_IsGroundedOnHit(
				in this,
				ref context,
				ref baseContext,
				in hit,
				in stepHandling,
				groundingEvaluationType
			);
		}

		public void OnMovementHit(
			ref LuaCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext,
			ref KinematicCharacterHit hit,
			ref float3 remainingMovementDirection,
			ref float remainingMovementLength,
			float3 originalVelocityDirection,
			float hitDistance
		)
		{
			ref var characterBody = ref characterAspect.CharacterBody.ValueRW;
			ref var characterPosition = ref characterAspect.LocalTransform.ValueRW.Position;

			var stepHandling = BasicStepAndSlopeHandlingParameters.GetDefault();

			characterAspect.Default_OnMovementHit(
				in this,
				ref context,
				ref baseContext,
				ref characterBody,
				ref characterPosition,
				ref hit,
				ref remainingMovementDirection,
				ref remainingMovementLength,
				originalVelocityDirection,
				hitDistance,
				stepHandling.StepHandling,
				stepHandling.MaxStepHeight,
				stepHandling.CharacterWidthForStepGroundingCheck
			);
		}

		public void OverrideDynamicHitMasses(
			ref LuaCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext,
			ref PhysicsMass characterMass,
			ref PhysicsMass otherMass,
			BasicHit hit
		)
		{
			// No override
		}

		public void ProjectVelocityOnHits(
			ref LuaCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext,
			ref float3 velocity,
			ref bool characterIsGrounded,
			ref BasicHit characterGroundHit,
			in DynamicBuffer<KinematicVelocityProjectionHit> hits,
			float3 originalVelocityDirection
		)
		{
			var stepHandling = BasicStepAndSlopeHandlingParameters.GetDefault();

			characterAspect.Default_ProjectVelocityOnHits(
				ref velocity,
				ref characterIsGrounded,
				ref characterGroundHit,
				in hits,
				originalVelocityDirection,
				stepHandling.ConstrainVelocityToGroundPlane
			);
		}
	}
}
