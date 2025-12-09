namespace LuaECS.Components
{
	using System;
	using Core;
	using Unity.Collections;
	using Unity.Entities;

	/// <summary>
	/// Persistent ID for Lua entity references.
	/// Unlike Entity, this ID remains stable across structural changes.
	/// </summary>
	public struct LuaEntityId : IComponentData
	{
		public int value;
	}

	[Serializable]
	public struct LuaScriptAssetReference : IEquatable<LuaScriptAssetReference>
	{
		public FixedString64Bytes scriptId;

		public bool IsValid => !scriptId.IsEmpty;

		public string Path => scriptId.ToString();

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

			scriptId = new FixedString64Bytes(normalized);
		}

		public void Clear()
		{
			scriptId.Clear();
		}

		public FixedString64Bytes AsFixedString() => scriptId;

		public override string ToString() => scriptId.ToString();

		public bool Equals(LuaScriptAssetReference other) => scriptId.Equals(other.scriptId);

		public override bool Equals(object obj) =>
			obj is LuaScriptAssetReference other && Equals(other);

		public override int GetHashCode() => scriptId.GetHashCode();

		public static implicit operator FixedString64Bytes(LuaScriptAssetReference reference) =>
			reference.scriptId;

		public static implicit operator LuaScriptAssetReference(FixedString64Bytes scriptId) =>
			new() { scriptId = scriptId };
	}

	/// <summary>
	/// Request to add a Lua script to an entity.
	/// Processed by LuaScriptFulfillmentSystem which creates the VM state
	/// and adds the script to the LuaScript buffer.
	/// Requests persist with Fulfilled flag for tracking.
	/// </summary>
	public struct LuaScriptRequest : IBufferElementData
	{
		public FixedString64Bytes scriptName;
		public Hash128 requestHash;
		public bool fulfilled;
	}

	/// <summary>
	/// Initialized Lua script with VM state.
	/// This is a cleanup buffer - entity won't be destroyed until this buffer is removed.
	/// </summary>
	public struct LuaScript : ICleanupBufferElementData
	{
		public FixedString64Bytes scriptName;
		public int stateRef;
		public int entityIndex;
		public Hash128 requestHash;
		public bool disabled;
		public LuaTickGroup tickGroup;
	}
}
