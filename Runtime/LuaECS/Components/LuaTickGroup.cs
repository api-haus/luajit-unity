namespace LuaECS.Components
{
	/// <summary>
	/// Defines which system group a Lua script's OnTick callback runs in.
	/// Scripts specify this via @tick: annotation (defaults to Variable).
	/// </summary>
	public enum LuaTickGroup : byte
	{
		/// <summary>
		/// Default. Runs every frame in SimulationSystemGroup.
		/// </summary>
		Variable = 0,

		/// <summary>
		/// Runs at fixed timestep in FixedStepSimulationSystemGroup.
		/// Use for physics-dependent logic.
		/// </summary>
		Fixed,

		/// <summary>
		/// Runs before PhysicsSystemGroup.
		/// Use for setting up physics inputs.
		/// </summary>
		BeforePhysics,

		/// <summary>
		/// Runs after PhysicsSystemGroup.
		/// Use for reacting to physics results.
		/// </summary>
		AfterPhysics,

		/// <summary>
		/// Runs after TransformSystemGroup.
		/// Use for logic that needs final world positions.
		/// </summary>
		AfterTransform,
	}
}
