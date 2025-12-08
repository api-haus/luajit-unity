namespace LuaCharacters.ThirdPerson
{
	using Unity.Entities;
	using UnityEngine;
	using UnityEngine.InputSystem;

	/// <summary>
	/// Scene hook that opt-in to Lua player bootstrap when baked.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class LuaPlayerBootstrapAuthoring : MonoBehaviour
	{
		[Tooltip("Input actions asset passed to Lua player bootstrap.")]
		public InputActionAsset playerInputActions;

		class Baker : Unity.Entities.Baker<LuaPlayerBootstrapAuthoring>
		{
			public override void Bake(LuaPlayerBootstrapAuthoring authoring)
			{
				var entity = GetEntity(TransformUsageFlags.None);
				AddComponent<LuaPlayerBootstrapConfig>(entity);

				if (authoring.playerInputActions != null)
				{
					DependsOn(authoring.playerInputActions);
					AddComponentObject(
						entity,
						new ThirdPersonPlayerInputActions { InputActionAsset = authoring.playerInputActions }
					);
				}
				else
				{
					Debug.LogWarning(
						"[LuaPlayerBootstrapAuthoring] Missing player input actions asset; Lua input will not initialize."
					);
				}
			}
		}
	}
}
