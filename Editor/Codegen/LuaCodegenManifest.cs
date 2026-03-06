#if UNITY_EDITOR
namespace LuaGame.Editor.Codegen
{
	using System;
	using System.Collections.Generic;
	using UnityEngine;

	[CreateAssetMenu(
		fileName = "LuaCodegenManifest",
		menuName = "LuaGame/Codegen Manifest"
	)]
	public class LuaCodegenManifest : ScriptableObject
	{
		[Serializable]
		public class Entry
		{
			[SerializeReference]
			public string typeName;

			public string luaName;
			public bool readOnly;

			[NonSerialized]
			Type m_CachedType;

			public Type componentType
			{
				get
				{
					if (m_CachedType != null)
						return m_CachedType;

					if (string.IsNullOrEmpty(typeName))
						return null;

					foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
					{
						m_CachedType = asm.GetType(typeName);
						if (m_CachedType != null)
							return m_CachedType;
					}
					return null;
				}
			}
		}

		public List<Entry> entries = new();
		public string outputDirectory;
	}
}
#endif
