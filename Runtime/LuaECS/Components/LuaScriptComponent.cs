namespace LuaECS.Components
{
	using System;
	using LuaECS.Core;
	using Unity.Collections;
	using Unity.Entities;

	/// <summary>
	/// Persistent ID for Lua entity references.
	/// Unlike Entity, this ID remains stable across structural changes.
	/// </summary>
	public struct LuaEntityId : IComponentData
	{
		public int Value;
	}

	[Serializable]
	public struct LuaScriptAssetReference : IEquatable<LuaScriptAssetReference>
	{
		public FixedString64Bytes ScriptId;

		public bool IsValid => !ScriptId.IsEmpty;

		public string Path => ScriptId.ToString();

		public void SetPath(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				Clear();
				return;
			}

			if (
				!LuaScriptPathUtility.TryNormalizeScriptId(path, out var normalized, out var _)
				|| string.IsNullOrEmpty(normalized)
			)
			{
				Clear();
				return;
			}

			ScriptId = new FixedString64Bytes(normalized);
		}

		public void Clear()
		{
			ScriptId.Clear();
		}

		public FixedString64Bytes AsFixedString() => ScriptId;

		public override string ToString() => ScriptId.ToString();

		public bool Equals(LuaScriptAssetReference other) => ScriptId.Equals(other.ScriptId);

		public override bool Equals(object obj) =>
			obj is LuaScriptAssetReference other && Equals(other);

		public override int GetHashCode() => ScriptId.GetHashCode();

		public static implicit operator FixedString64Bytes(LuaScriptAssetReference reference) =>
			reference.ScriptId;

		public static implicit operator LuaScriptAssetReference(FixedString64Bytes scriptId) =>
			new() { ScriptId = scriptId };
	}

	/// <summary>
	/// Request to add a Lua script to an entity.
	/// Processed by LuaScriptFulfillmentSystem which creates the VM state
	/// and adds the script to the LuaScript buffer.
	/// Requests persist with Fulfilled flag for tracking.
	/// </summary>
	public struct LuaScriptRequest : IBufferElementData
	{
		public FixedString64Bytes ScriptName;
		public Hash128 RequestHash;
		public bool Fulfilled;
	}

	/// <summary>
	/// Initialized Lua script with VM state.
	/// This is a cleanup buffer - entity won't be destroyed until this buffer is removed.
	/// </summary>
	public struct LuaScript : ICleanupBufferElementData
	{
		public FixedString64Bytes ScriptName;
		public int StateRef;
		public int EntityIndex;
		public Hash128 RequestHash;
		public bool Disabled;
	}
}
