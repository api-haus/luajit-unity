namespace LuaGame.Components
{
	using Unity.Entities;

	/// <summary>
	/// Marker component for entities that can receive damage.
	/// Add this tag to entities that should participate in damage zone/projectile queries.
	/// </summary>
	public struct LuaDamageableTag : IComponentData { }
}
