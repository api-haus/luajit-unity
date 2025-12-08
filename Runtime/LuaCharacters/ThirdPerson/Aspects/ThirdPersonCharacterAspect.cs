namespace LuaCharacters.ThirdPerson.Aspects
{
	using Authoring;
	using Components;
	using Unity.CharacterController;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Physics;
	using static Unity.Mathematics.math;

	public struct ThirdPersonCharacterUpdateContext
	{
		// Here, you may add additional global data for your character updates, such as ComponentLookups, Singletons, NativeCollections, etc...
		// The data you add here will be accessible in your character updates and all of your character "callbacks".

		public void OnSystemCreate(ref SystemState state)
		{
			// Get lookups
		}

		public void OnSystemUpdate(ref SystemState state)
		{
			// Update lookups
		}
	}

	public readonly partial struct ThirdPersonCharacterAspect
		: IAspect,
			IKinematicCharacterProcessor<ThirdPersonCharacterUpdateContext>
	{
		public readonly KinematicCharacterAspect characterAspect;
		public readonly RefRW<ThirdPersonCharacterComponent> characterComponent;
		public readonly RefRW<ThirdPersonCharacterControl> characterControl;

		[Optional]
		public readonly RefRW<CustomGravity> customGravity;

		public void PhysicsUpdate(
			ref ThirdPersonCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext
		)
		{
			ref var characterComponent = ref this.characterComponent.ValueRW;
			ref var characterBody = ref characterAspect.CharacterBody.ValueRW;
			ref var characterPosition = ref characterAspect.LocalTransform.ValueRW.Position;
			var hasCustomGravity = customGravity.IsValid && customGravity.ValueRO.gravityMultiplier > 0;

			// First phase of default character update
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

			// Update desired character velocity after grounding was detected, but before doing additional processing that depends on velocity
			HandleVelocityControl(ref context, ref baseContext);

			// Second phase of default character update
			characterAspect.Update_PreventGroundingFromFutureSlopeChange(
				in this,
				ref context,
				ref baseContext,
				ref characterBody,
				in characterComponent.stepAndSlopeHandling
			);
			characterAspect.Update_GroundPushing(
				in this,
				ref context,
				ref baseContext,
				hasCustomGravity ? customGravity.ValueRO.gravity : characterComponent.gravity
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
			ref ThirdPersonCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext
		)
		{
			var deltaTime = baseContext.Time.DeltaTime;
			ref var characterBody = ref characterAspect.CharacterBody.ValueRW;
			ref var characterComponent = ref this.characterComponent.ValueRW;
			ref var characterControl = ref this.characterControl.ValueRW;
			var hasCustomGravity = customGravity.IsValid && customGravity.ValueRO.gravityMultiplier > 0;

			// Rotate move input and velocity to take into account parent rotation
			if (characterBody.ParentEntity != Entity.Null)
			{
				characterControl.moveVector = rotate(
					characterBody.RotationFromParent,
					characterControl.moveVector
				);
				characterBody.RelativeVelocity = rotate(
					characterBody.RotationFromParent,
					characterBody.RelativeVelocity
				);
			}

			if (characterBody.IsGrounded)
			{
				// Move on ground
				var targetVelocity = characterControl.moveVector * characterComponent.groundMaxSpeed;
				CharacterControlUtilities.StandardGroundMove_Interpolated(
					ref characterBody.RelativeVelocity,
					targetVelocity,
					characterComponent.groundedMovementSharpness,
					deltaTime,
					characterBody.GroundingUp,
					characterBody.GroundHit.Normal
				);

				// Jump
				if (characterControl.jump)
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
				// Move in air
				var airAcceleration = characterControl.moveVector * characterComponent.airAcceleration;
				if (lengthsq(airAcceleration) > 0f)
				{
					var tmpVelocity = characterBody.RelativeVelocity;
					CharacterControlUtilities.StandardAirMove(
						ref characterBody.RelativeVelocity,
						airAcceleration,
						characterComponent.airMaxSpeed,
						characterBody.GroundingUp,
						deltaTime,
						false
					);

					// Cancel air acceleration from input if we would hit a non-grounded surface (prevents air-climbing slopes at high air accelerations)
					if (
						characterComponent.preventAirAccelerationAgainstUngroundedHits
						&& characterAspect.MovementWouldHitNonGroundedObstruction(
							in this,
							ref context,
							ref baseContext,
							characterBody.RelativeVelocity * deltaTime,
							out var hit
						)
					)
					{
						characterBody.RelativeVelocity = tmpVelocity;
					}
				}

				// Gravity
				CharacterControlUtilities.AccelerateVelocity(
					ref characterBody.RelativeVelocity,
					hasCustomGravity ? customGravity.ValueRO.gravity : characterComponent.gravity,
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
			ref ThirdPersonCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext
		)
		{
			var deltaTime = baseContext.Time.DeltaTime;
			ref var characterBody = ref characterAspect.CharacterBody.ValueRW;
			ref var characterComponent = ref this.characterComponent.ValueRW;
			ref var characterControl = ref this.characterControl.ValueRW;
			ref var characterRotation = ref characterAspect.LocalTransform.ValueRW.Rotation;
			var hasCustomGravity = customGravity.IsValid && customGravity.ValueRO.gravityMultiplier > 0;

			// Add rotation from parent body to the character rotation
			// (this is for allowing a rotating moving platform to rotate your character as well, and handle interpolation properly)
			KinematicCharacterUtilities.AddVariableRateRotationFromFixedRateRotation(
				ref characterRotation,
				characterBody.RotationFromParent,
				baseContext.Time.DeltaTime,
				characterBody.LastPhysicsUpdateDeltaTime
			);

			// Rotate towards move direction
			if (characterComponent.relativeMovement && lengthsq(characterControl.moveVector) > 0f)
			{
				CharacterControlUtilities.SlerpRotationTowardsDirectionAroundUp(
					ref characterRotation,
					deltaTime,
					normalizesafe(characterControl.moveVector),
					MathUtilities.GetUpFromRotation(characterRotation),
					characterComponent.rotationSharpness
				);
			}

			if (hasCustomGravity)
			{
				CharacterControlUtilities.SlerpCharacterUpTowardsDirection(
					ref characterRotation,
					deltaTime,
					normalizesafe(-customGravity.ValueRO.gravity),
					3.0f
				);
			}
		}

		#region Character Processor Callbacks

		public void UpdateGroundingUp(
			ref ThirdPersonCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext
		)
		{
			ref var characterBody = ref characterAspect.CharacterBody.ValueRW;

			characterAspect.Default_UpdateGroundingUp(ref characterBody);
		}

		public bool CanCollideWithHit(
			ref ThirdPersonCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext,
			in BasicHit hit
		)
		{
			return PhysicsUtilities.IsCollidable(hit.Material);
		}

		public bool IsGroundedOnHit(
			ref ThirdPersonCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext,
			in BasicHit hit,
			int groundingEvaluationType
		)
		{
			var characterComponent = this.characterComponent.ValueRO;

			return characterAspect.Default_IsGroundedOnHit(
				in this,
				ref context,
				ref baseContext,
				in hit,
				in characterComponent.stepAndSlopeHandling,
				groundingEvaluationType
			);
		}

		public void OnMovementHit(
			ref ThirdPersonCharacterUpdateContext context,
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
			var characterComponent = this.characterComponent.ValueRO;

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
				characterComponent.stepAndSlopeHandling.StepHandling,
				characterComponent.stepAndSlopeHandling.MaxStepHeight,
				characterComponent.stepAndSlopeHandling.CharacterWidthForStepGroundingCheck
			);
		}

		public void OverrideDynamicHitMasses(
			ref ThirdPersonCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext,
			ref PhysicsMass characterMass,
			ref PhysicsMass otherMass,
			BasicHit hit
		)
		{
			// Custom mass overrides
		}

		public void ProjectVelocityOnHits(
			ref ThirdPersonCharacterUpdateContext context,
			ref KinematicCharacterUpdateContext baseContext,
			ref float3 velocity,
			ref bool characterIsGrounded,
			ref BasicHit characterGroundHit,
			in DynamicBuffer<KinematicVelocityProjectionHit> velocityProjectionHits,
			float3 originalVelocityDirection
		)
		{
			var characterComponent = this.characterComponent.ValueRO;

			characterAspect.Default_ProjectVelocityOnHits(
				ref velocity,
				ref characterIsGrounded,
				ref characterGroundHit,
				in velocityProjectionHits,
				originalVelocityDirection,
				characterComponent.stepAndSlopeHandling.ConstrainVelocityToGroundPlane
			);
		}

		#endregion
	}
}
