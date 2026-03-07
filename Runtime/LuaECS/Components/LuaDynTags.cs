namespace LuaECS.Components
{
	using Unity.Entities;

	// @formatter:off — generated tag pool for Lua-defined component types
	public struct LuaDynTag0  : IComponentData { } public struct LuaDynTag1  : IComponentData { }
	public struct LuaDynTag2  : IComponentData { } public struct LuaDynTag3  : IComponentData { }
	public struct LuaDynTag4  : IComponentData { } public struct LuaDynTag5  : IComponentData { }
	public struct LuaDynTag6  : IComponentData { } public struct LuaDynTag7  : IComponentData { }
	public struct LuaDynTag8  : IComponentData { } public struct LuaDynTag9  : IComponentData { }
	public struct LuaDynTag10 : IComponentData { } public struct LuaDynTag11 : IComponentData { }
	public struct LuaDynTag12 : IComponentData { } public struct LuaDynTag13 : IComponentData { }
	public struct LuaDynTag14 : IComponentData { } public struct LuaDynTag15 : IComponentData { }
	public struct LuaDynTag16 : IComponentData { } public struct LuaDynTag17 : IComponentData { }
	public struct LuaDynTag18 : IComponentData { } public struct LuaDynTag19 : IComponentData { }
	public struct LuaDynTag20 : IComponentData { } public struct LuaDynTag21 : IComponentData { }
	public struct LuaDynTag22 : IComponentData { } public struct LuaDynTag23 : IComponentData { }
	public struct LuaDynTag24 : IComponentData { } public struct LuaDynTag25 : IComponentData { }
	public struct LuaDynTag26 : IComponentData { } public struct LuaDynTag27 : IComponentData { }
	public struct LuaDynTag28 : IComponentData { } public struct LuaDynTag29 : IComponentData { }
	public struct LuaDynTag30 : IComponentData { } public struct LuaDynTag31 : IComponentData { }
	public struct LuaDynTag32 : IComponentData { } public struct LuaDynTag33 : IComponentData { }
	public struct LuaDynTag34 : IComponentData { } public struct LuaDynTag35 : IComponentData { }
	public struct LuaDynTag36 : IComponentData { } public struct LuaDynTag37 : IComponentData { }
	public struct LuaDynTag38 : IComponentData { } public struct LuaDynTag39 : IComponentData { }
	public struct LuaDynTag40 : IComponentData { } public struct LuaDynTag41 : IComponentData { }
	public struct LuaDynTag42 : IComponentData { } public struct LuaDynTag43 : IComponentData { }
	public struct LuaDynTag44 : IComponentData { } public struct LuaDynTag45 : IComponentData { }
	public struct LuaDynTag46 : IComponentData { } public struct LuaDynTag47 : IComponentData { }
	public struct LuaDynTag48 : IComponentData { } public struct LuaDynTag49 : IComponentData { }
	public struct LuaDynTag50 : IComponentData { } public struct LuaDynTag51 : IComponentData { }
	public struct LuaDynTag52 : IComponentData { } public struct LuaDynTag53 : IComponentData { }
	public struct LuaDynTag54 : IComponentData { } public struct LuaDynTag55 : IComponentData { }
	public struct LuaDynTag56 : IComponentData { } public struct LuaDynTag57 : IComponentData { }
	public struct LuaDynTag58 : IComponentData { } public struct LuaDynTag59 : IComponentData { }
	public struct LuaDynTag60 : IComponentData { } public struct LuaDynTag61 : IComponentData { }
	public struct LuaDynTag62 : IComponentData { } public struct LuaDynTag63 : IComponentData { }
	// @formatter:on

	/// <summary>
	/// Cleanup marker for entities with Lua-defined components.
	/// Survives entity destruction so that the cleanup system can scrub Lua-side data.
	/// </summary>
	public struct LuaDataCleanup : ICleanupComponentData
	{
		public int entityId;
	}
}
