namespace LuaECS.Systems
{
	using Components;
	using Core;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics;
	using Unity.Transforms;

	[BurstCompile]
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateAfter(typeof(LuaScriptingSystem))]
	public partial struct LuaCommandProcessorSystem : ISystem
	{
		public void OnCreate(ref SystemState state) { }

		public void OnUpdate(ref SystemState state)
		{
			var commands = LuaECSBridge.FlushCommands();
			if (commands.Length == 0)
			{
				commands.Dispose();
				return;
			}

			var deltaTime = SystemAPI.Time.DeltaTime;

			// Separate movement commands for parallel processing
			var moveCommands = new NativeList<LuaCommand>(commands.Length, Allocator.TempJob);
			var entityToCommandIndex = new NativeHashMap<Entity, int>(commands.Length, Allocator.TempJob);

			// ECB for structural changes (main thread only)
			var ecb = new EntityCommandBuffer(Allocator.Temp);

			// Sort commands: movement goes to parallel job, others processed immediately
			foreach (var cmd in commands)
			{
				switch (cmd.type)
				{
					case LuaCommandType.MOVE:
					case LuaCommandType.MOVE_TOWARD:
						if (
							state.EntityManager.Exists(cmd.target)
							&& state.EntityManager.HasComponent<LocalTransform>(cmd.target)
						)
						{
							// Only add if not already in map (last command wins)
							if (!entityToCommandIndex.ContainsKey(cmd.target))
							{
								entityToCommandIndex.Add(cmd.target, moveCommands.Length);
								moveCommands.Add(cmd);
							}
							else
							{
								// Update existing command
								var idx = entityToCommandIndex[cmd.target];
								moveCommands[idx] = cmd;
							}
						}
						break;

					case LuaCommandType.ATTACK:
						ProcessAttackCommand(ref state, cmd);
						break;

					case LuaCommandType.DESTROY_ENTITY:
						var toDestroy = cmd.secondaryTarget != Entity.Null ? cmd.secondaryTarget : cmd.target;
						if (state.EntityManager.Exists(toDestroy))
						{
							Log.Debug(
								"[LuaCmd] Destroying entity via DestroyEntity command: {0}",
								toDestroy.Index
							);
							// Remove LuaEntityId to trigger staged cleanup flow
							// This ensures VM state is properly released before entity destruction
							if (state.EntityManager.HasComponent<LuaEntityId>(toDestroy))
							{
								ecb.RemoveComponent<LuaEntityId>(toDestroy);
							}
							else
							{
								// No LuaEntityId - destroy directly (non-Lua entity or already staged)
								ecb.DestroyEntity(toDestroy);
							}
						}
						else
						{
							Log.Warning(
								"[LuaCmd] DestroyEntity: entity doesn't exist, SecondaryTarget={0}, Target={1}",
								cmd.secondaryTarget.Index,
								cmd.target.Index
							);
						}
						break;
				}
			}

			// Schedule parallel movement job if we have movement commands
			if (moveCommands.Length > 0)
			{
				var moveJob = new ProcessMoveCommandsJob
				{
					deltaTime = deltaTime,
					commands = moveCommands.AsArray(),
					entityToCommandIndex = entityToCommandIndex,
				};

				// Schedule parallel across all entities with LocalTransform
				state.Dependency = moveJob.ScheduleParallel(state.Dependency);
				state.Dependency.Complete(); // Complete before ECB playback
			}

			// Playback structural changes
			ecb.Playback(state.EntityManager);
			ecb.Dispose();

			// Cleanup
			moveCommands.Dispose();
			entityToCommandIndex.Dispose();
			commands.Dispose();
		}

		void ProcessAttackCommand(ref SystemState state, LuaCommand cmd)
		{
			if (!state.EntityManager.Exists(cmd.secondaryTarget))
				return;

			if (!state.EntityManager.HasBuffer<LuaEvent>(cmd.secondaryTarget))
				return;

			var eventBuffer = state.EntityManager.GetBuffer<LuaEvent>(cmd.secondaryTarget);
			eventBuffer.Add(
				new LuaEvent
				{
					eventName = "on_attacked",
					source = cmd.target,
					target = cmd.secondaryTarget,
					intParam = cmd.intParam,
				}
			);
		}
	}

	/// <summary>
	/// Burst-compiled parallel job for processing movement commands.
	/// Runs across all entities with LocalTransform, only processes those with pending commands.
	/// </summary>
	[BurstCompile]
	public partial struct ProcessMoveCommandsJob : IJobEntity
	{
		public float deltaTime;

		[ReadOnly]
		public NativeArray<LuaCommand> commands;

		[ReadOnly]
		public NativeHashMap<Entity, int> entityToCommandIndex;

		void Execute(Entity entity, ref LocalTransform transform)
		{
			if (!entityToCommandIndex.TryGetValue(entity, out var cmdIndex))
				return;

			var cmd = commands[cmdIndex];

			if (cmd.type == LuaCommandType.MOVE_TOWARD)
			{
				var direction = cmd.position - transform.Position;
				var distance = math.length(direction);

				if (distance > 0.01f)
				{
					var normalizedDir = direction / distance;
					var moveDistance = math.min(cmd.floatParam * deltaTime, distance);
					transform.Position += normalizedDir * moveDistance;

					var targetRot = quaternion.LookRotationSafe(normalizedDir, math.up());
					transform.Rotation = math.slerp(transform.Rotation, targetRot, deltaTime * 10f);
				}
			}
			else if (cmd.type == LuaCommandType.MOVE)
			{
				transform.Position = cmd.position;
			}
		}
	}
}
