namespace LuaECS.Systems.Tick
{
	using Components;
	using Unity.Entities;
	using Unity.Physics.Systems;

	/// <summary>
	/// Processes Lua scripts with @tick: after_physics annotation.
	/// Runs after PhysicsSystemGroup.
	/// Use for reacting to physics simulation results (collisions, new positions).
	/// </summary>
	[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
	[UpdateAfter(typeof(PhysicsSystemGroup))]
	public partial class LuaAfterPhysicsTickSystem : LuaTickSystemBase
	{
		protected override LuaTickGroup GetTickGroup() => LuaTickGroup.AfterPhysics;
	}
}
