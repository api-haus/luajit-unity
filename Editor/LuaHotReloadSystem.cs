#if UNITY_EDITOR
namespace LuaGame.Editor
{
	using System;
	using System.Collections.Concurrent;
	using System.IO;
	using LuaVM.Core;
	using Unity.Entities;
	using Unity.Logging;
	using UnityEngine;

	[UpdateInGroup(typeof(InitializationSystemGroup))]
	public partial class LuaHotReloadSystem : SystemBase
	{
		FileSystemWatcher m_ScriptsWatcher;
		FileSystemWatcher m_DataWatcher;
		readonly ConcurrentQueue<string> m_ReloadQueue = new();
		bool m_Initialized;

		protected override void OnCreate()
		{
			m_Initialized = false;
		}

		protected override void OnStartRunning()
		{
			if (m_Initialized)
				return;

			var luaPath = Path.Combine(Application.streamingAssetsPath, "lua");
			var scriptsPath = Path.Combine(luaPath, "scripts");
			var dataPath = Path.Combine(luaPath, "data");

			if (!Directory.Exists(luaPath))
			{
				Directory.CreateDirectory(luaPath);
				Directory.CreateDirectory(scriptsPath);
				Directory.CreateDirectory(dataPath);
			}

			if (Directory.Exists(scriptsPath))
			{
				m_ScriptsWatcher = CreateWatcher(scriptsPath);
			}

			if (Directory.Exists(dataPath))
			{
				m_DataWatcher = CreateWatcher(dataPath);
			}

			m_Initialized = true;
		}

		FileSystemWatcher CreateWatcher(string path)
		{
			var watcher = new FileSystemWatcher(path)
			{
				Filter = "*.lua",
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
				EnableRaisingEvents = true,
				IncludeSubdirectories = true,
			};

			watcher.Changed += OnFileChanged;
			watcher.Created += OnFileChanged;

			return watcher;
		}

		void OnFileChanged(object sender, FileSystemEventArgs e)
		{
			if (e.ChangeType == WatcherChangeTypes.Changed || e.ChangeType == WatcherChangeTypes.Created)
			{
				m_ReloadQueue.Enqueue(e.FullPath);
			}
		}

		protected override void OnUpdate()
		{
			var vm = LuaVMManager.Instance;
			if (vm == null || !vm.IsValid)
				return;

			while (m_ReloadQueue.TryDequeue(out var filePath))
			{
				try
				{
					var fileName = Path.GetFileNameWithoutExtension(filePath);
					var directory = Path.GetDirectoryName(filePath);

					if (directory != null && directory.Contains("data"))
					{
						fileName = $"data.{fileName}";
					}

					if (vm.ReloadScript(fileName))
					{
						Log.Debug($"[LuaHotReload] Reloaded: {fileName}");
					}
				}
				catch (Exception ex)
				{
					Log.Error($"[LuaHotReload] Error reloading {filePath}: {ex.Message}");
				}
			}
		}

		protected override void OnDestroy()
		{
			if (m_ScriptsWatcher != null)
			{
				m_ScriptsWatcher.EnableRaisingEvents = false;
				m_ScriptsWatcher.Dispose();
				m_ScriptsWatcher = null;
			}

			if (m_DataWatcher != null)
			{
				m_DataWatcher.EnableRaisingEvents = false;
				m_DataWatcher.Dispose();
				m_DataWatcher = null;
			}
		}
	}
}
#endif
