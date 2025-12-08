namespace LuaCharacters.Core
{
	using Unity.Burst;
	using Unity.Burst.Intrinsics;
	using Unity.CharacterController;
	using Unity.Entities;
	using Unity.Physics;
	using Unity.Transforms;

	[UpdateInGroup(typeof(KinematicCharacterPhysicsUpdateGroup))]
	[BurstCompile]
	public partial struct LuaCharacterPhysicsUpdateSystem : ISystem
	{
		EntityQuery m_CharacterQuery;
		LuaCharacterUpdateContext m_Context;
		KinematicCharacterUpdateContext m_BaseContext;

		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			m_CharacterQuery = KinematicCharacterUtilities
				.GetBaseCharacterQueryBuilder()
				.WithAll<LuaCharacterComponent, LuaCharacterControl>()
				.Build(ref state);

			m_Context = new LuaCharacterUpdateContext();
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
			m_BaseContext.OnSystemUpdate(
				ref state,
				SystemAPI.Time,
				SystemAPI.GetSingleton<PhysicsWorldSingleton>()
			);

			var job = new LuaCharacterPhysicsUpdateJob
			{
				context = m_Context,
				baseContext = m_BaseContext,
			};
			job.ScheduleParallel();
		}

		[BurstCompile]
		[WithAll(typeof(Simulate))]
		public partial struct LuaCharacterPhysicsUpdateJob : IJobEntity, IJobEntityChunkBeginEnd
		{
			public LuaCharacterUpdateContext context;
			public KinematicCharacterUpdateContext baseContext;

			void Execute(LuaCharacterAspect characterAspect)
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
	[UpdateBefore(typeof(TransformSystemGroup))]
	[BurstCompile]
	public partial struct LuaCharacterVariableUpdateSystem : ISystem
	{
		EntityQuery m_CharacterQuery;
		LuaCharacterUpdateContext m_Context;
		KinematicCharacterUpdateContext m_BaseContext;

		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			m_CharacterQuery = KinematicCharacterUtilities
				.GetBaseCharacterQueryBuilder()
				.WithAll<LuaCharacterComponent, LuaCharacterControl>()
				.Build(ref state);

			m_Context = new LuaCharacterUpdateContext();
			m_BaseContext = new KinematicCharacterUpdateContext();
			m_BaseContext.OnSystemCreate(ref state);

			state.RequireForUpdate(m_CharacterQuery);
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			m_BaseContext.OnSystemUpdate(
				ref state,
				SystemAPI.Time,
				SystemAPI.GetSingleton<PhysicsWorldSingleton>()
			);

			var job = new LuaCharacterVariableUpdateJob
			{
				context = m_Context,
				baseContext = m_BaseContext,
			};
			job.ScheduleParallel();
		}

		[BurstCompile]
		[WithAll(typeof(Simulate))]
		public partial struct LuaCharacterVariableUpdateJob : IJobEntity
		{
			public LuaCharacterUpdateContext context;
			public KinematicCharacterUpdateContext baseContext;

			void Execute(LuaCharacterAspect characterAspect)
			{
				characterAspect.VariableUpdate(ref context, ref baseContext);
			}
		}
	}
}
