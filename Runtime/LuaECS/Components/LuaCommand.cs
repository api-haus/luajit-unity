namespace LuaECS.Components
{
	using Unity.Entities;
	using Unity.Mathematics;

	public enum LuaCommandType : byte
	{
		None = 0,
		Move,
		MoveToward,
		Attack,
		Deposit,
		DepositAll,
		Pickup,
		SetState,
		SpawnEntity,
		DestroyEntity,
	}

	public struct LuaCommand : IBufferElementData
	{
		public Entity Target;
		public LuaCommandType Type;
		public float3 Position;
		public float FloatParam;
		public int IntParam;
		public Entity SecondaryTarget;
	}

	public struct LuaCommandBuffer : IComponentData
	{
		public int CommandCount;
	}
}
