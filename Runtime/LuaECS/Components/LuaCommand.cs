namespace LuaECS.Components
{
	using Unity.Entities;
	using Unity.Mathematics;

	public enum LuaCommandType : byte
	{
		NONE = 0,
		MOVE,
		MOVE_TOWARD,
		ATTACK,
		DEPOSIT,
		DEPOSIT_ALL,
		PICKUP,
		SET_STATE,
		SPAWN_ENTITY,
		DESTROY_ENTITY,
	}

	public struct LuaCommand : IBufferElementData
	{
		public Entity target;
		public LuaCommandType type;
		public float3 position;
		public float floatParam;
		public int intParam;
		public Entity secondaryTarget;
	}

	public struct LuaCommandBuffer : IComponentData
	{
		public int commandCount;
	}
}
