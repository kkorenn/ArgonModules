using System.Collections.Generic;
using Argon;
using Argon.UI;
using HarmonyLib;
using MelonLoader;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Argon.ArgonI18n;

namespace ExampleModule
{
    /// <summary>
    /// Tour of everything Argon offers a module: every TabBuilder widget, saved settings, driving widgets
    /// from code, notifications, Harmony patches (fixed and toggled at runtime), custom Unity UI, typing
    /// detection, translations and hot reload (rebuild while the game runs).
    /// </summary>
    public class ExampleModule : ArgonModule
    {
        public override string Id => "example.module";
        public override string Title => "example.title"; // translation key, see Lang/*.json
        // Runs in any game. To limit a module to specific games (matched against the game's product name):
        // public override string[] Games => new[] { "A Dance of Fire and Ice" };
        // IconFile defaults to "icon.png" (embedded in the csproj). IconText is used if the icon is missing.

        // ---- Settings: each is saved to UserData/Argon/Modules/Config/example.module.cfg as soon as it changes.
        private MelonPreferences_Entry<bool> _countKeys = null!;
        private MelonPreferences_Entry<float> _volume = null!;
        private MelonPreferences_Entry<int> _difficulty = null!;
        private MelonPreferences_Entry<int> _mode = null!;
        private MelonPreferences_Entry<string> _nickname = null!;

        /// <summary>Lets the static Harmony patches below reach the module.</summary>
        internal static ExampleModule? Instance;

        private MelonPreferences_Entry<bool> _logKeys = null!;
        private MelonPreferences_Entry<int> _transition = null!;

        // ---- Live stats, fed by this module's own Harmony patch (see GameHooks) and Unity's scene event.
        internal int _keys;
        internal string _lastKey = "-";
        private int _scenes;
        private string _scene = "-";

        // ---- Widgets kept to update them later. Rebuilt (replaced) whenever the language changes.
        private TextMeshProUGUI? _stats, _greeting, _typing;
        private ArgonSwitch? _countSwitch;
        private ArgonChips? _modeChips;
        private ArgonDropdown? _difficultyDropdown;
        private RectTransform? _barFill;

        public override void OnLoad()
        {
            _countKeys = Setting("CountKeys", true, "Count key presses");
            _volume = Setting("Volume", 0.8f, "Example slider value");
            _difficulty = Setting("Difficulty", 1, "Example dropdown index");
            _mode = Setting("Mode", 0, "Example chips index");
            _nickname = Setting("Nickname", "", "Example text field value");

            _logKeys = Setting("LogKeys", false, "Patch Input.GetKeyDown at runtime to log key presses");
            _transition = Setting("SubTabAnimation", 0, "0 = slide, 1 = fade, 2 = scale");
            Instance = this;

            // [HarmonyPatch] classes (GameHooks) are already applied by Argon. This one is patched on demand.
            if (_logKeys.Value) SetKeyLogging(true);

            // Something outside Argon: must be undone in OnUnload, or the old code keeps running after a reload.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

            // Opened fires when the window opens (not when F10 brings it back); Closed when it closes.
            Tab.Opened += RefreshStats;
            Tab.Closed += () => Log.Msg("Example window closed");
            ArgonI18n.LanguageChanged += () => Log.Msg($"Language is now {ArgonI18n.Language}");
        }

        public override void OnUnload()
        {
            // Argon removes our Harmony patches, tab handlers and LanguageChanged handlers by itself.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
            Log.Msg("Unloading (file deleted or rebuilt)");
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode _)
        {
            _scene = scene.name;
            _scenes++;
        }

        internal bool CountKeys => _countKeys.Value;

        // Runtime patching: Harmony is this module's own instance, so anything patched here is undone on unload.
        private static readonly System.Reflection.MethodInfo GetKeyDown =
            AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) });
        private static readonly System.Reflection.MethodInfo LogKey =
            AccessTools.Method(typeof(RuntimePatches), nameof(RuntimePatches.LogKey));

        private void SetKeyLogging(bool on)
        {
            if (on) Harmony.Patch(GetKeyDown, postfix: new HarmonyMethod(LogKey));
            else Harmony.Unpatch(GetKeyDown, LogKey); // just this patch: GameHooks patches the same method
        }

        public override void BuildTab(TabBuilder root)
        {
            // Sub-tabs: a strip under the window title, each with its own page. The transition is picked in "Extras".
            root.SubTabTransition = (SubTabTransition)_transition.Value;

            root.SubTab(T("example.sub.widgets"), ui =>
            {
                // ---- Images: any PNG/JPG embedded in the module (Images/*.png in the csproj).
                ui.Image(LoadImage("banner.png"), 130, cornerRadius: 12);

                // ---- Text
                ui.Header(T("example.section.text"));
                ui.Label(T("example.text.plain"));
                ui.Label(T("example.text.rich"), 16); // TMP rich text: <b>, <i>, <color>, <size>...
                _greeting = ui.Label(Greeting());

                // ---- Buttons
                ui.Header(T("example.section.buttons"));
                ui.Button(T("example.sayHello"), () => Notify(T("example.hello"), T("example.helloBody", _keys)));
                ui.Button(T("example.button.hideBar"), () => ArgonUI.BarVisible = false); // F10 brings it back

                // ---- Saved settings: every widget type, each bound to a Setting.
                ui.Header(T("example.section.settings"));
                _countSwitch = ui.Toggle(T("example.countKeys"), _countKeys.Value, v => _countKeys.Value = v);
                ui.Slider(T("example.volume"), 0f, 1f, _volume.Value, v => _volume.Value = v, "0%");
                _difficultyDropdown = ui.Dropdown(T("example.difficulty"),
                    new[] { T("example.difficulty.easy"), T("example.difficulty.normal"), T("example.difficulty.hard") },
                    _difficulty.Value, i => _difficulty.Value = i);
                _modeChips = ui.Chips(new[] { T("example.mode.normal"), T("example.mode.practice"), T("example.mode.auto") },
                    _mode.Value, i => _mode.Value = i);

                // ---- Text input: while it has focus, the game ignores key presses (ArgonInput.IsTyping).
                ui.Header(T("example.section.input"));
                var field = ui.Input(T("example.nickname"), v =>
                {
                    _nickname.Value = v;
                    if (_greeting) _greeting!.text = Greeting();
                });
                field.SetTextWithoutNotify(_nickname.Value);
                _typing = ui.Label("", 15);

                // ---- Driving widgets from code: setting Value/Selected animates them and runs their callback.
                ui.Header(T("example.section.code"));
                ui.Button(T("example.code.flip"), () => _countSwitch!.Value = !_countSwitch.Value);
                ui.Button(T("example.code.nextMode"), () => _modeChips!.Selected = (_modeChips.Selected + 1) % 3);
                ui.Button(T("example.code.hardest"), () => _difficultyDropdown!.Selected = 2);
                ui.Button(T("example.code.widen"), () =>
                    _difficultyDropdown!.Width = _difficultyDropdown.Width == null ? 260f : null); // fixed width vs fit-to-text
                ui.Button(T("example.code.widenList"), () =>
                    _difficultyDropdown!.PopupWidth = _difficultyDropdown.PopupWidth == null ? 260f : null); // option list width
            });

            root.SubTab(T("example.sub.notify"), ui =>
            {
                // ---- Notifications
                ui.Header(T("example.section.notify"));
                ui.Button(T("example.notify.title"), () => ArgonUI.Notify(T("example.notify.titleOnly")));
                ui.Button(T("example.notify.body"), () => ArgonUI.Notify(T("example.notify.titleOnly"), T("example.notify.bodyText")));
                ui.Button(T("example.notify.icon"), () => Notify(T("example.notify.withIcon"), T("example.notify.iconText")));
                ui.Button(T("example.notify.long"), () =>
                    ArgonUI.Notify(T("example.notify.longTitle"), T("example.notify.longText"),
                        ArgonUI.LoadIcon(GetType().Assembly, "icon.png"), seconds: 10));
            });

            root.SubTab(T("example.sub.game"), ui =>
            {
                // ---- Harmony patches made while the game runs.
                ui.Header(T("example.section.patches"));
                ui.Label(T("example.patches.hint"), 15);
                ui.Toggle(T("example.patches.logKeys"), _logKeys.Value, v =>
                {
                    _logKeys.Value = v;
                    SetKeyLogging(v);
                });

                // ---- Live stats from our own patch and plain Unity (refreshed every frame while the window is open, see OnUpdate).
                ui.Header(T("example.section.stats"));
                _stats = ui.Label("", 16);
                RefreshStats();
            });

            root.SubTab(T("example.sub.extras"), ui =>
            {
                // ---- Sub-tabs: change the switch animation (Tab.Rebuild re-runs BuildTab), or switch from code.
                ui.Header(T("example.section.subtabs"));
                ui.Dropdown(T("example.subtabs.animation"),
                    new[] { T("example.subtabs.slide"), T("example.subtabs.fade"), T("example.subtabs.scale") },
                    _transition.Value, i =>
                    {
                        _transition.Value = i;
                        Tab.Rebuild();
                    });
                ui.Button(T("example.subtabs.first"), () => Tab.SubTab = 0);

                // ---- Custom UI: a Row is an empty RectTransform you fill with plain Unity UI.
                ui.Header(T("example.section.custom"));
                ui.Label(T("example.custom.hint"), 15);
                var row = ui.Row(14);
                var track = new GameObject("Track", typeof(RectTransform)).AddComponent<Image>();
                track.transform.SetParent(row, false);
                track.color = new Color(1, 1, 1, 0.08f);
                Stretch(track.rectTransform);
                var fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
                fill.transform.SetParent(track.transform, false);
                fill.color = new Color(0.47f, 0.56f, 1f);
                _barFill = fill.rectTransform;
                Stretch(_barFill);

                // ---- Images again: same file, smaller, square corners. LoadImage caches, so this is free.
                ui.Header(T("example.section.images"));
                ui.Label(T("example.images.hint"), 15);
                ui.Image(LoadImage("banner.png"), 60);

                // ---- Translations
                ui.Header(T("example.section.i18n"));
                ui.Label(T("example.i18n.current", ArgonI18n.Language), 15);
                ui.Label(T("example.i18n.hint"), 15);
            });
        }

        public override void OnUpdate()
        {
            if (!Tab.IsOpen) return;
            RefreshStats();
            if (_typing) _typing!.text = ArgonInput.IsTyping ? T("example.input.typing") : T("example.input.idle");
            if (_barFill) _barFill!.anchorMax = new Vector2(Mathf.PingPong(Time.unscaledTime * 0.5f, 1f), 1);
        }

        private void RefreshStats()
        {
            if (_stats) _stats!.text = T("example.stats", _keys, _lastKey, _scenes, _scene,
                Mathf.RoundToInt(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)), Time.timeScale == 0f ? T("example.yes") : T("example.no"));
        }

        private string Greeting() =>
            string.IsNullOrEmpty(_nickname.Value) ? T("example.greeting.anon") : T("example.greeting.name", _nickname.Value);

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    // [HarmonyPatch] classes in a module are applied automatically when it loads (and removed when it unloads).
    // Patch whatever you need; this one feeds the live stats. Input.GetKeyDown is plain Unity, so it works in any game
    // that uses Unity's classic input (games on the new Input System never call it).
    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), typeof(KeyCode))]
    internal static class GameHooks
    {
        private static readonly NewPresses Counted = new();

        private static void Postfix(KeyCode key, bool __result)
        {
            if (!__result || ExampleModule.Instance is not { } m || !Counted.IsNew(key)) return;
            m._lastKey = key.ToString();
            if (m.CountKeys) m._keys++;
        }
    }

    // Not a [HarmonyPatch] class: patched and unpatched at runtime by the "Log key presses" toggle.
    internal static class RuntimePatches
    {
        private static readonly NewPresses Logged = new();

        internal static void LogKey(KeyCode key, bool __result)
        {
            if (__result && Logged.IsNew(key)) ExampleModule.Instance?.Log.Msg($"Pressed {key}");
        }
    }

    /// <summary>Games often check the same key several times a frame; this lets each press through once.</summary>
    internal sealed class NewPresses
    {
        private int _frame = -1;
        private readonly HashSet<KeyCode> _seen = new();

        public bool IsNew(KeyCode key)
        {
            if (_frame != Time.frameCount)
            {
                _frame = Time.frameCount;
                _seen.Clear();
            }
            return _seen.Add(key);
        }
    }
}
