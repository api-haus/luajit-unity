namespace LuaECS.Components
{
	using Unity.Collections;
	using Unity.Entities;

	public struct LuaEvent : IBufferElementData
	{
		public FixedString32Bytes EventName;
		public Entity Source;
		public Entity Target;
		public int IntParam;
		public float FloatParam;
	}
}
