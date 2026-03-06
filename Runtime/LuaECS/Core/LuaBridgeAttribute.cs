using System;

namespace LuaECS.Core
{
	[AttributeUsage(AttributeTargets.Struct)]
	public sealed class LuaBridgeAttribute : Attribute
	{
		public string LuaName { get; }
		public bool ReadOnly { get; }

		public LuaBridgeAttribute(string luaName = null, bool readOnly = false)
		{
			LuaName = luaName;
			ReadOnly = readOnly;
		}
	}

	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class LuaBridgeForAttribute : Attribute
	{
		public Type ComponentType { get; }
		public string LuaName { get; }
		public bool ReadOnly { get; }

		public LuaBridgeForAttribute(
			Type componentType,
			string luaName = null,
			bool readOnly = false
		)
		{
			ComponentType = componentType;
			LuaName = luaName;
			ReadOnly = readOnly;
		}
	}
}
