namespace LuaECS.Systems.Tick
{
	using Components;
	using Unity.Entities;
	using Unity.Physics.Systems;

	/// <summary>
	/// Processes Lua scripts with @tick: before_physics annotation.
	/// Runs before PhysicsSystemGroup.
	/// Use for setting up physics inputs (forces, velocities) before simulation.
	/// </summary>
	[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
	[UpdateBefore(typeof(PhysicsSystemGroup))]
	public partial class LuaBeforePhysicsTickSystem : LuaTickSystemBase
	{
		protected override LuaTickGroup GetTickGroup() => LuaTickGroup.BeforePhysics;
	}
}
