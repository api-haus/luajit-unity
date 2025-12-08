namespace LuaVM.Tests
{
	using Core;
	using NUnit.Framework;
	using UnityEngine;

	/// <summary>
	/// Unit tests for LuaScriptLoader utility.
	/// </summary>
	public class LuaScriptLoaderTest
	{
		[Test]
		public void ValidateScriptId_ValidInput_ReturnsSuccess()
		{
			var result = LuaScriptLoader.ValidateScriptId("test_script");

			Assert.IsTrue(result.isValid);
			Assert.AreEqual("test_script", result.scriptId.ToString());
			Assert.AreEqual(LuaScriptSourceType.STRING, result.sourceType);
		}

		[Test]
		public void ValidateScriptId_EmptyScriptId_ReturnsFailure()
		{
			var result = LuaScriptLoader.ValidateScriptId("");

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("Script ID cannot be empty", result.error.ToString());
		}

		[Test]
		public void ValidateScriptId_NullScriptId_ReturnsFailure()
		{
			var result = LuaScriptLoader.ValidateScriptId(null);

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("Script ID cannot be empty", result.error.ToString());
		}

		[Test]
		public void ValidateScriptId_LongScriptId_ReturnsFailure()
		{
			var longId = new string('a', 100);

			var result = LuaScriptLoader.ValidateScriptId(longId);

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("Script ID too long (max 64 bytes)", result.error.ToString());
		}

		[Test]
		public void ValidateTextAsset_NullAsset_ReturnsFailure()
		{
			var result = LuaScriptLoader.ValidateTextAsset(null);

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("TextAsset is null", result.error.ToString());
		}

		[Test]
		public void ValidateTextAsset_WithCustomId_NullAsset_ReturnsFailure()
		{
			var result = LuaScriptLoader.ValidateTextAsset("custom_id", null);

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("TextAsset is null", result.error.ToString());
		}

		[Test]
		public void ValidateTextAsset_WithCustomId_EmptyId_ReturnsFailure()
		{
			var asset = new TextAsset("print('test')");
			var result = LuaScriptLoader.ValidateTextAsset("", asset);

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("Script ID cannot be empty", result.error.ToString());
		}

		[Test]
		public void FromStreamingAssets_EmptyPath_ReturnsFailure()
		{
			var result = LuaScriptLoader.FromStreamingAssets("");

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("Relative path cannot be empty", result.error.ToString());
		}

		[Test]
		public void FromStreamingAssets_NullPath_ReturnsFailure()
		{
			var result = LuaScriptLoader.FromStreamingAssets(null);

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("Relative path cannot be empty", result.error.ToString());
		}

		[Test]
		public void FromStreamingAssets_NonExistentFile_ReturnsFailure()
		{
			var result = LuaScriptLoader.FromStreamingAssets("nonexistent_script_12345");

			Assert.IsFalse(result.isValid);
			Assert.IsTrue(result.error.ToString().Contains("not found"));
		}

		[Test]
		public void FromStreamingAssets_NormalizesPath()
		{
			// Since file doesn't exist, result is failure, but error message contains normalized path
			var result = LuaScriptLoader.FromStreamingAssets("subfolder\\script.lua");

			Assert.IsFalse(result.isValid, "Should fail for non-existent file");
			Assert.IsTrue(
				result.error.ToString().Contains("subfolder/script"),
				"Error message should contain normalized path"
			);
		}

		[Test]
		public void FromFile_EmptyPath_ReturnsFailure()
		{
			var result = LuaScriptLoader.FromFile("");

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("File path cannot be empty", result.error.ToString());
		}

		[Test]
		public void FromFile_NullPath_ReturnsFailure()
		{
			var result = LuaScriptLoader.FromFile(null);

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("File path cannot be empty", result.error.ToString());
		}

		[Test]
		public void FromFile_NonExistentFile_ReturnsFailure()
		{
			var result = LuaScriptLoader.FromFile("/nonexistent/path/script.lua");

			Assert.IsFalse(result.isValid);
			Assert.IsTrue(result.error.ToString().Contains("not found"));
		}

		[Test]
		public void FromFile_WithCustomId_EmptyId_ReturnsFailure()
		{
			var result = LuaScriptLoader.FromFile("", "/some/path.lua");

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("Script ID cannot be empty", result.error.ToString());
		}

		[Test]
		public void TryReadSource_InvalidResult_ReturnsFalse()
		{
			var loadResult = LuaScriptLoadResult.Failure("test error");

			var success = LuaScriptLoader.TryReadSource(loadResult, out var source);

			Assert.IsFalse(success);
			Assert.IsNull(source);
		}

		[Test]
		public void TryReadSource_StringSource_ReturnsFalse()
		{
			var loadResult = LuaScriptLoader.ValidateScriptId("test");

			var success = LuaScriptLoader.TryReadSource(loadResult, out var source);

			Assert.IsFalse(success);
			Assert.IsNull(source);
		}

		[Test]
		public void LuaScriptLoadResult_Success_CreatesValidResult()
		{
			var result = LuaScriptLoadResult.Success(
				new Unity.Collections.FixedString64Bytes("test"),
				LuaScriptSourceType.STREAMING_ASSETS
			);

			Assert.IsTrue(result.isValid);
			Assert.AreEqual("test", result.scriptId.ToString());
			Assert.AreEqual(LuaScriptSourceType.STREAMING_ASSETS, result.sourceType);
			Assert.IsTrue(result.error.IsEmpty);
		}

		[Test]
		public void LuaScriptLoadResult_Failure_CreatesInvalidResult()
		{
			var result = LuaScriptLoadResult.Failure("Test error message");

			Assert.IsFalse(result.isValid);
			Assert.AreEqual("Test error message", result.error.ToString());
			Assert.AreEqual(LuaScriptSourceType.NONE, result.sourceType);
		}
	}
}
