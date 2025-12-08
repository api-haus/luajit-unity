#if UNITY_EDITOR
namespace LuaGame.Editor
{
	using LuaECS.Components;
	using LuaECS.Core;
	using UnityEditor;
	using UnityEngine;

	[CustomPropertyDrawer(typeof(LuaScriptAssetReference))]
	sealed class LuaScriptAssetReferenceDrawer : PropertyDrawer
	{
		static readonly GUIContent s_scriptFieldContent = new(
			"Lua Script",
			"Drag a .lua file from Assets/StreamingAssets/lua/scripts"
		);

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);

			var showMixed = property.hasMultipleDifferentValues;
			var reference = showMixed ? default : (LuaScriptAssetReference)property.boxedValue;
			var storedPath = showMixed ? string.Empty : reference.Path;

			var lineHeight = EditorGUIUtility.singleLineHeight;
			var spacing = EditorGUIUtility.standardVerticalSpacing;

			var pathRect = new Rect(position.x, position.y, position.width, lineHeight);
			var assetRect = new Rect(position.x, pathRect.yMax + spacing, position.width, lineHeight);
			var helpRect = new Rect(
				position.x,
				assetRect.yMax + spacing,
				position.width,
				lineHeight * 2f
			);

			EditorGUI.showMixedValue = showMixed;
			EditorGUI.BeginChangeCheck();
			var manualPath = EditorGUI.DelayedTextField(pathRect, label, storedPath);
			if (EditorGUI.EndChangeCheck())
			{
				if (string.IsNullOrWhiteSpace(manualPath))
				{
					reference.Clear();
					property.boxedValue = reference;
					storedPath = string.Empty;
					showMixed = false;
				}
				else if (
					LuaScriptPathUtility.TryNormalizeScriptId(manualPath, out var normalized, out var error)
				)
				{
					reference.SetPath(normalized);
					property.boxedValue = reference;
					storedPath = reference.Path;
					showMixed = false;
				}
				else
				{
					Debug.LogWarning(error);
				}
			}

			EditorGUI.BeginChangeCheck();
			var newAsset = EditorGUI.ObjectField(
				assetRect,
				s_scriptFieldContent,
				null,
				typeof(DefaultAsset),
				false
			);
			if (EditorGUI.EndChangeCheck())
			{
				if (newAsset == null)
				{
					reference.Clear();
					property.boxedValue = reference;
					storedPath = string.Empty;
					showMixed = false;
				}
				else if (
					LuaScriptPathUtility.TryGetScriptId(newAsset, out var scriptId, out var assetError)
				)
				{
					reference.SetPath(scriptId);
					property.boxedValue = reference;
					storedPath = reference.Path;
					showMixed = false;
				}
				else
				{
					Debug.LogWarning(assetError);
				}
			}

			EditorGUI.showMixedValue = false;

			if (!showMixed && TryGetValidationMessage(storedPath, out var message, out var type))
				EditorGUI.HelpBox(helpRect, message, type);

			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			var lineHeight = EditorGUIUtility.singleLineHeight;
			var spacing = EditorGUIUtility.standardVerticalSpacing;
			var height = (lineHeight * 2f) + spacing;

			if (!property.hasMultipleDifferentValues)
			{
				var reference = (LuaScriptAssetReference)property.boxedValue;
				if (TryGetValidationMessage(reference.Path, out _, out _))
					height += (lineHeight * 2f) + spacing;
			}

			return height;
		}

		static bool TryGetValidationMessage(string storedPath, out string message, out MessageType type)
		{
			if (string.IsNullOrEmpty(storedPath))
			{
				message = string.Empty;
				type = MessageType.None;
				return false;
			}

			if (!LuaScriptPathUtility.TryNormalizeScriptId(storedPath, out var normalized, out var error))
			{
				message = error;
				type = MessageType.Error;
				return true;
			}

			if (LuaScriptPathUtility.ScriptExists(normalized))
			{
				message = string.Empty;
				type = MessageType.None;
				return false;
			}

			var assetPath = LuaScriptPathUtility.GetProjectRelativePath(normalized);
			message = $"Lua script not found at '{assetPath}'.";
			type = MessageType.Warning;
			return true;
		}
	}
}
#endif
