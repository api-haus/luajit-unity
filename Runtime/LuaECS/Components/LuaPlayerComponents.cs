namespace LuaECS.Components
{
	using System;
	using Unity.Entities;

	[Serializable]
	public struct LuaPlayerTag : IComponentData
	{
		public int playerId;
	}
}
