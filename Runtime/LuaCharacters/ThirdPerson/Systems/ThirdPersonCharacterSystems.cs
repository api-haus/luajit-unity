namespace LuaCharacters.ThirdPerson
{
	using Unity.Burst;
	using Unity.Burst.Intrinsics;
	using Unity.CharacterController;
	using Unity.Entities;
	using Unity.Physics;
	using Unity.Transforms;

	[UpdateInGroup(typeof(KinematicCharacterPhysicsUpdateGroup))]
	[BurstCompile]
	public partial struct ThirdPersonCharacterPhysicsUpdateSystem : ISystem
	{
		EntityQuery m_CharacterQuery;
		ThirdPersonCharacterUpdateContext m_Context;
		KinematicCharacterUpdateContext m_BaseContext;

		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			m_CharacterQuery = KinematicCharacterUtilities
				.GetBaseCharacterQueryBuilder()
				.WithAll<ThirdPersonCharacterComponent, ThirdPersonCharacterControl>()
				.Build(ref state);

			m_Context = new ThirdPersonCharacterUpdateContext();
			m_Context.OnSystemCreate(ref state);
			m_BaseContext = new KinematicCharacterUpdateContext();
			m_BaseContext.OnSystemCreate(ref state);

			state.RequireForUpdate(m_CharacterQuery);
			state.RequireForUpdate<PhysicsWorldSingleton>();
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			m_Context.OnSystemUpdate(ref state);
			m_BaseContext.OnSystemUpdate(
				ref state,
				SystemAPI.Time,
				SystemAPI.GetSingleton<PhysicsWorldSingleton>()
			);

			var job = new ThirdPersonCharacterPhysicsUpdateJob
			{
				context = m_Context,
				baseContext = m_BaseContext,
			};
			job.ScheduleParallel();
		}

		[BurstCompile]
		[WithAll(typeof(Simulate))]
		public partial struct ThirdPersonCharacterPhysicsUpdateJob : IJobEntity, IJobEntityChunkBeginEnd
		{
			public ThirdPersonCharacterUpdateContext context;
			public KinematicCharacterUpdateContext baseContext;

			void Execute(ThirdPersonCharacterAspect characterAspect)
			{
				characterAspect.PhysicsUpdate(ref context, ref baseContext);
			}

			public bool OnChunkBegin(
				in ArchetypeChunk chunk,
				int unfilteredChunkIndex,
				bool useEnabledMask,
				in v128 chunkEnabledMask
			)
			{
				baseContext.EnsureCreationOfTmpCollections();
				return true;
			}

			public void OnChunkEnd(
				in ArchetypeChunk chunk,
				int unfilteredChunkIndex,
				bool useEnabledMask,
				in v128 chunkEnabledMask,
				bool chunkWasExecuted
			) { }
		}
	}

	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
	//[UpdateAfter(typeof(ThirdPersonPlayerVariableStepControlSystem))] // not required as this system only handles camera input
	[UpdateBefore(typeof(TransformSystemGroup))]
	[BurstCompile]
	public partial struct ThirdPersonCharacterVariableUpdateSystem : ISystem
	{
		EntityQuery m_CharacterQuery;
		ThirdPersonCharacterUpdateContext m_Context;
		KinematicCharacterUpdateContext m_BaseContext;

		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			m_CharacterQuery = KinematicCharacterUtilities
				.GetBaseCharacterQueryBuilder()
				.WithAll<ThirdPersonCharacterComponent, ThirdPersonCharacterControl>()
				.Build(ref state);

			m_Context = new ThirdPersonCharacterUpdateContext();
			m_Context.OnSystemCreate(ref state);
			m_BaseContext = new KinematicCharacterUpdateContext();
			m_BaseContext.OnSystemCreate(ref state);

			state.RequireForUpdate(m_CharacterQuery);
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			m_Context.OnSystemUpdate(ref state);
			m_BaseContext.OnSystemUpdate(
				ref state,
				SystemAPI.Time,
				SystemAPI.GetSingleton<PhysicsWorldSingleton>()
			);

			var job = new ThirdPersonCharacterVariableUpdateJob
			{
				context = m_Context,
				baseContext = m_BaseContext,
			};
			job.ScheduleParallel();
		}

		[BurstCompile]
		[WithAll(typeof(Simulate))]
		public partial struct ThirdPersonCharacterVariableUpdateJob
			: IJobEntity,
				IJobEntityChunkBeginEnd
		{
			public ThirdPersonCharacterUpdateContext context;
			public KinematicCharacterUpdateContext baseContext;

			void Execute(ThirdPersonCharacterAspect characterAspect)
			{
				characterAspect.VariableUpdate(ref context, ref baseContext);
			}

			public bool OnChunkBegin(
				in ArchetypeChunk chunk,
				int unfilteredChunkIndex,
				bool useEnabledMask,
				in v128 chunkEnabledMask
			)
			{
				baseContext.EnsureCreationOfTmpCollections();
				return true;
			}

			public void OnChunkEnd(
				in ArchetypeChunk chunk,
				int unfilteredChunkIndex,
				bool useEnabledMask,
				in v128 chunkEnabledMask,
				bool chunkWasExecuted
			) { }
		}
	}
}
