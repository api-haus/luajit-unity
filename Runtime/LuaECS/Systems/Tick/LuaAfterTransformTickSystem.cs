namespace LuaECS.Systems.Tick
{
	using Components;
	using Unity.Entities;
	using Unity.Transforms;

	/// <summary>
	/// Processes Lua scripts with @tick: after_transform annotation.
	/// Runs after TransformSystemGroup.
	/// Use for logic that needs final world positions after all transform updates.
	/// </summary>
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateAfter(typeof(TransformSystemGroup))]
	public partial class LuaAfterTransformTickSystem : LuaTickSystemBase
	{
		protected override LuaTickGroup GetTickGroup() => LuaTickGroup.AfterTransform;
	}
}
