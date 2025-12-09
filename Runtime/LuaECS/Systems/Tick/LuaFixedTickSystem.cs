namespace LuaECS.Systems.Tick
{
	using Components;
	using Unity.Entities;

	/// <summary>
	/// Processes Lua scripts with @tick: fixed annotation.
	/// Runs in FixedStepSimulationSystemGroup at fixed timestep.
	/// Use for physics-dependent logic that needs consistent timing.
	/// </summary>
	[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
	public partial class LuaFixedTickSystem : LuaTickSystemBase
	{
		protected override LuaTickGroup GetTickGroup() => LuaTickGroup.Fixed;
	}
}
