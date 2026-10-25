// Stand-ins for the slice of the Quartz core API that the ported TUF logic uses, backed by Argon/MelonLoader.
// Keeping the same names lets the logic files under Logic/ stay as close to Quartz's as possible.
#nullable enable
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Argon;
using MelonLoader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Quartz.Core
{
    public static class Diag
    {
        public static void Ignore(Exception e) { /* Quartz only logged these in debug builds */ }
        public static void Warn(Exception e, string where) => MainCore.Log.Wrn($"[{where}] {e}");
    }

    public static class MainCore
    {
        public static readonly LogShim Log = new();
        public static readonly TrShim Tr = new();
        public static readonly PathShim Paths = new();
        public static readonly HostShim Host = new();

        public sealed class HostShim
        {
            public string ModsPath => MelonLoader.Utils.MelonEnvironment.ModsDirectory;
        }

        /// <summary>Persistent GameObject for helpers (preview pump, loading cover). Created by the module, destroyed on unload.</summary>
        public static GameObject Root { get; internal set; } = null!;

        public sealed class LogShim
        {
            internal MelonLogger.Instance? Target;
            public void Msg(string message) => Target?.Msg(Clean(message));
            public void Wrn(string message) => Target?.Warning(Clean(message));
            public void Err(string message) => Target?.Error(Clean(message));

            // The logger is already named after the module; drop Quartz's own "[TUF] " prefix.
            private static string Clean(string message) => message.StartsWith("[TUF] ") ? message.Substring(6) : message;
        }

        public sealed class TrShim
        {
            /// <summary>Translation from the module's Lang/*.json, or the fallback when the key is missing.</summary>
            public string Get(string key, string fallback)
            {
                var text = ArgonI18n.T(key);
                return text == key ? fallback : text;
            }
        }

        public sealed class PathShim
        {
            /// <summary>UserData/Argon/Modules/Data/TUF: downloaded levels and the thumbnail cache.</summary>
            public string TufPath => Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "Argon", "Modules", "Data", "TUF");
            /// <summary>UserData/Argon/Modules/Config/tuf: Settings.json and Installed.json (set from the module's ConfigFolder).</summary>
            public string TufConfigPath { get; internal set; } = "";
            public string TufLevelsPath => Path.Combine(TufPath, "Levels");
        }
    }
}

namespace Quartz.Async
{
    /// <summary>Runs callbacks from background tasks on Unity's main thread (drained by the module every frame).</summary>
    public static class MainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new();
        public static void Enqueue(Action action) => Queue.Enqueue(action);

        internal static void Drain()
        {
            while (Queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Quartz.Core.MainCore.Log.Err($"Main-thread callback threw: {e}"); }
            }
        }

        internal static void Clear()
        {
            while (Queue.TryDequeue(out _)) { }
        }
    }
}

namespace Quartz.IO.Interface
{
    public interface ISettingsFile
    {
        JToken Serialize();
        void Deserialize(JToken token);
    }
}

namespace Quartz.IO
{
    using Quartz.Core;
    using Quartz.IO.Interface;

    /// <summary>JSON settings file. RequestSave coalesces writes; everything is flushed on unload.</summary>
    public sealed class SettingsFile<T> where T : class, ISettingsFile, new()
    {
        public T Data { get; } = new();
        public readonly string Path;
        private bool _dirty;
        internal static readonly List<Action> Flushers = new();

        public SettingsFile(string path)
        {
            Path = path;
            Flushers.Add(() => { if (_dirty) Save(); });
        }

        public bool Load()
        {
            try
            {
                if (!File.Exists(Path)) return false;
                Data.Deserialize(JToken.Parse(File.ReadAllText(Path)));
                return true;
            }
            catch (Exception e)
            {
                MainCore.Log.Err($"Failed to load '{Path}': {e.Message}");
                return false;
            }
        }

        public void RequestSave()
        {
            _dirty = true;
            SaveScheduler.Request(this);
        }

        public bool Save()
        {
            _dirty = false;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var tmp = Path + ".tmp";
                File.WriteAllText(tmp, Data.Serialize().ToString(Formatting.Indented));
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(tmp, Path);
                return true;
            }
            catch (Exception e)
            {
                MainCore.Log.Err($"Failed to save '{Path}': {e.Message}");
                return false;
            }
        }

        public static void FlushAll()
        {
            foreach (var flush in Flushers) flush();
        }
    }

    public static class SettingsRegistry
    {
        public static void Unregister(object handle) { /* our SettingsFile isn't registered globally */ }
    }

    /// <summary>Writes requested saves about a second later on the main thread, so bursts of changes write once.</summary>
    internal static class SaveScheduler
    {
        private static readonly HashSet<object> Pending = new();
        private static float _due = -1;

        public static void Request(object file)
        {
            lock (Pending) Pending.Add(file);
            if (_due < 0) _due = 1f; // seconds; counted down in Tick
        }

        public static void Tick(float dt)
        {
            if (_due < 0) return;
            _due -= dt;
            if (_due > 0) return;
            _due = -1;
            object[] files;
            lock (Pending)
            {
                files = Pending.ToArray();
                Pending.Clear();
            }
            foreach (var f in files) f.GetType().GetMethod("Save")!.Invoke(f, null);
        }
    }
}

namespace Quartz.UI
{
    /// <summary>Quartz's main menu; in Argon the bar plays that role.</summary>
    public static class UICore
    {
        public static bool IsOpen => Argon.UI.ArgonUI.BarVisible;
        public static void Open(bool animate) => Argon.UI.ArgonUI.BarVisible = true;
        public static void Close(bool animate) => Argon.UI.ArgonUI.BarVisible = false;
    }
}

namespace Quartz.UI.Utility
{
    public static class UIColors
    {
        public static readonly Color PanelBG = new(0.07f, 0.07f, 0.10f, 0.94f);
    }

    public static class UnityUtils
    {
        /// <summary>Full-screen overlay canvas under <paramref name="parent"/> at the given sort order.</summary>
        public static GameObject CreateOverlayCanvas(string name, Transform parent, int sortingOrder, out UnityEngine.UI.GraphicRaycaster raycaster)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            raycaster = go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            return go;
        }
    }
}

namespace Quartz.Features.AprilFools
{
    /// <summary>Quartz's April Fools quiz; not part of the Argon port, so it just continues.</summary>
    public static class QuizGate
    {
        public static void GateMain(string code, string difficulty, Action proceed) => proceed();
        public static void GateChart(string chart, string difficulty, Action proceed) => proceed();
    }
}
namespace Quartz.Compat.Interface
{
    public interface IRuntimeService
    {
        void Initialize();
        void Dispose();
    }
}

namespace Quartz.Game.Stats
{
    public static class GameStats
    {
        /// <summary>Inside a level and not paused.</summary>
        public static bool InGame
        {
            get
            {
                var c = scrController.instance;
                return c && !c.paused && !ADOBase.isLevelSelect && !ADOBase.isCLS;
            }
        }
    }
}

namespace Quartz.Compat.Game
{
    /// <summary>Quartz's version-tolerant game wrappers, as direct calls for the current game version.</summary>
    public static class GameApi
    {
        private static readonly BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        public static IDictionary? WorldTable()
        {
            var t = typeof(ADOBase).Assembly.GetType("WorldData");
            return (t?.GetField("dict", Any)?.GetValue(null) ?? t?.GetProperty("dict", Any)?.GetValue(null)) as IDictionary;
        }

        public static int WorldLevelCount(string world)
        {
            var table = WorldTable();
            if (string.IsNullOrEmpty(world) || table == null || !table.Contains(world)) return -1;
            var entry = table[world];
            var f = entry?.GetType().GetField("levelCount", Any);
            return f?.GetValue(entry) is int n ? n : -1;
        }

        public static bool IsInternalLevelCode(string code)
        {
            var m = typeof(scrController).GetMethod("IsWorldAndLevelInternalLevel", Any);
            if (m != null && m.GetParameters().Length == 1) return m.Invoke(null, new object[] { code }) is true;
            return false;
        }

        public static bool LoadSceneWithTransition(WipeDirection direction)
        {
            var loader = typeof(ADOBase).GetProperty("loader", Any)?.GetValue(null) ?? typeof(ADOBase).GetField("loader", Any)?.GetValue(null);
            var m = loader?.GetType().GetMethod("LoadSceneWithTransition", Any);
            if (m == null) return false;
            m.Invoke(loader, new object[] { direction });
            return true;
        }

        public static string SceneLevelSelect =>
            typeof(ADOBase).Assembly.GetType("GCNS")?.GetField("sceneLevelSelect", Any)?.GetValue(null) as string ?? "scnLevelSelect";
    }
}

namespace Quartz.UI.Generator
{
    public static class GenerateUI
    {
        public static string Tr(string key, string fallback) => Quartz.Core.MainCore.Tr.Get(key, fallback);
    }
}
