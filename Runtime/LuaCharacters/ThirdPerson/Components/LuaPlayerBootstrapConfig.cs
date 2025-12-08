namespace LuaCharacters.ThirdPerson
{
	using Unity.Entities;

	/// <summary>
	/// Tag component baked into the scene to opt-in to Lua player bootstrap.
	/// </summary>
	public struct LuaPlayerBootstrapConfig : IComponentData { }
}
