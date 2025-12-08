namespace LuaECS.Authoring
{
	using Components;
	using UnityEngine;

	[RequireComponent(typeof(LuaScriptBufferAuthoring))]
	public class LuaScriptAuthoring : MonoBehaviour
	{
		[Tooltip("File name without .lua extension, relative to Assets/StreamingAssets/lua/scripts")]
		public LuaScriptAssetReference script;

		void Reset()
		{
			EnsureBufferAuthoring();
		}

		void OnValidate()
		{
			EnsureBufferAuthoring();
		}

		void EnsureBufferAuthoring()
		{
			if (TryGetComponent<LuaScriptBufferAuthoring>(out _))
				return;

			gameObject.AddComponent<LuaScriptBufferAuthoring>();
		}
	}
}
