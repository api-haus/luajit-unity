namespace LuaECS.Tests
{
	using System.IO;
	using LuaVM.Core;
	using UnityEngine;

	/// <summary>
	/// Utilities for Lua ECS tests.
	/// Provides access to test resources bundled with the package.
	/// </summary>
	public static class LuaTestUtilities
	{
		const string PACKAGE_NAME = "im.pala.unity.luagame";

		static string s_TestResourcesPath;

		/// <summary>
		/// Gets the path to the package's test Lua resources.
		/// Falls back to StreamingAssets if test resources aren't found.
		/// </summary>
		public static string TestLuaBasePath
		{
			get
			{
				if (s_TestResourcesPath != null)
					return s_TestResourcesPath;

				// Try package test resources first
				var packagePath = Path.GetFullPath($"Packages/{PACKAGE_NAME}/Tests~/Resources/lua");
				if (Directory.Exists(packagePath))
				{
					s_TestResourcesPath = packagePath;
					return s_TestResourcesPath;
				}

				// Fall back to StreamingAssets
				s_TestResourcesPath = Path.Combine(Application.streamingAssetsPath, "lua");
				return s_TestResourcesPath;
			}
		}

		/// <summary>
		/// Gets the path to the test scripts directory.
		/// </summary>
		public static string TestScriptsPath => Path.Combine(TestLuaBasePath, "scripts");

		/// <summary>
		/// Gets or creates a LuaVMManager configured to use test resources.
		/// Registers the test scripts path with the search path registry.
		/// </summary>
		public static LuaVMManager GetOrCreateTestVM()
		{
			// Always ensure test path is registered before VM operations
			var testScriptsPath = TestScriptsPath;
			if (Directory.Exists(testScriptsPath))
			{
				LuaScriptSearchPaths.AddSearchPath(testScriptsPath, priority: 0);
			}

			// If instance exists and is valid, return it
			if (LuaVMManager.Instance != null && LuaVMManager.Instance.IsValid)
				return LuaVMManager.Instance;

			// Create new VM with test resources path
			return new LuaVMManager(TestLuaBasePath);
		}

		/// <summary>
		/// Disposes the current VM instance and cleans up test search paths.
		/// Call in test teardown to ensure clean state.
		/// </summary>
		public static void DisposeVM()
		{
			// Remove test scripts path from search registry
			var testScriptsPath = TestScriptsPath;
			LuaScriptSearchPaths.RemoveSearchPath(testScriptsPath);

			LuaVMManager.Instance?.Dispose();
		}
	}
}
