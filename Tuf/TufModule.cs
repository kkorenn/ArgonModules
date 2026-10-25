using Argon;
using Argon.UI;
using Quartz.Async;
using Quartz.Core;
using Quartz.IO;
using UnityEngine;
using static Quartz.Features.Tuf.TufCards;

namespace Quartz.Features.Tuf;

/// <summary>
/// TUF (tuforums.com) level browser, ported from Quartz. The download/install/launch logic (Logic/) is Quartz's,
/// running on a small compatibility layer (Compat/); the window is rebuilt with Argon's UI (UI/).
/// </summary>
public sealed class TufModule : ArgonModule {
    public override string Id => "tuf";
    public override string Title => "TUF";
    public override string[] Games => new[] { "A Dance of Fire and Ice" }; // skipped in other games
    public override string IconFile => "TufLogo.png";

    private TufService tufService;
    private TufPackService packService;
    private TufLevelsPage levelsPage;
    private TufPacksView packsPage;
    private TufSettingsPage settingsPage;

    public override void OnLoad() {
        MainCore.Log.Target = Log;
        MainCore.Paths.TufConfigPath = ConfigFolder;
        MoveOldConfig();
        MainCore.Root = new GameObject("TUF");
        UnityEngine.Object.DontDestroyOnLoad(MainCore.Root);
        tufService = new TufService();
        packService = new TufPackService();
        tufService.Initialize();
        packService.Initialize();
    }

    // Settings.json/Installed.json used to live in Data/TUF next to the levels; they belong in Config/tuf.
    private void MoveOldConfig() {
        foreach(string name in new[] { "Settings.json", "Installed.json" }) {
            string from = Path.Combine(MainCore.Paths.TufPath, name), to = Path.Combine(ConfigFolder, name);
            try {
                if(File.Exists(from) && !File.Exists(to)) File.Move(from, to);
            } catch(Exception e) { Log.Warning($"Couldn't move {name} to the config folder: {e.Message}"); }
        }
    }

    public override void BuildTab(TabBuilder root) {
        // Runs again on language changes and reloads: drop the previous pages' service subscriptions first.
        levelsPage?.Detach();
        packsPage?.Detach();
        settingsPage?.Detach();
        root.SubTab(Tr("TUF_LEVELS", "Levels"), ui => levelsPage = new TufLevelsPage(ui, tufService));
        root.SubTab(Tr("TUF_PACKS", "Packs"), ui => packsPage = new TufPacksView(ui, packService));
        root.SubTab(Tr("TUF_SETTINGS", "Settings"), ui => settingsPage = new TufSettingsPage(ui, tufService));
    }

    public override void OnUpdate() {
        MainThread.Drain();
        SaveScheduler.Tick(Time.unscaledDeltaTime);
        if(!Tab.IsOpen) return;
        levelsPage?.Tick();
        packsPage?.Tick();
    }

    public override void OnUnload() {
        levelsPage?.Detach();
        packsPage?.Detach();
        settingsPage?.Detach();
        TufUnsavedDialog.Close();
        TufRollbackDialog.Close();
        packService?.Dispose();
        tufService?.Dispose(); // saves settings + install index
        SettingsFile<TufSettings>.FlushAll();
        TufPreviewCache.Clear(); // free thumbnail textures
        MainThread.Clear();
        if(MainCore.Root) UnityEngine.Object.Destroy(MainCore.Root);
    }
}
