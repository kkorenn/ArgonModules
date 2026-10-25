using Argon.UI;
using Quartz.Core;
using TMPro;
using UnityEngine;
using static Quartz.Features.Tuf.TufCards;

namespace Quartz.Features.Tuf;

/// <summary>The "Settings" sub-tab: TUFHelperLite link, and where downloaded levels live.</summary>
internal sealed class TufSettingsPage {
    private readonly TufService service;
    private readonly TextMeshProUGUI summary, path, status, notice;
    private readonly TextMeshProUGUI? linkedNote;

    public TufSettingsPage(TabBuilder ui, TufService service) {
        this.service = service;
        if(TufHelperLiteLink.Installed)
            ArgonUI.Tooltip(ui.Toggle(Tr("TUF_LINK_HELPER", "Link to TUFHelperLite folders"), service.LinkTufHelperLite, service.SetLinkTufHelperLite),
                Tr("DESC_TUF_LINK_HELPER", "Saves downloaded levels into TUFHelperLite's Downloads folder (as tuf-<id>) so both mods share one level library."));
        ArgonUI.Tooltip(ui.Toggle(Tr("TUF_PREVIEWS", "Level previews"), service.ShowPreviews, service.SetShowPreviews),
            Tr("DESC_TUF_PREVIEWS", "Show a blurred preview image behind each level, taken from its YouTube video. Turn off to skip the thumbnail downloads."));
        ArgonUI.Tooltip(ui.Toggle(Tr("TUF_GRID_VIEW", "Grid view"), service.GridView, service.SetGridView),
            Tr("DESC_TUF_GRID_VIEW", "Lay the level browser out as a grid of cards instead of one column of rows. The column count follows the window width."));

        ui.Header(Tr("TUF_LIBRARY", "Level library"));
        service.EnsureLibraryMeasured();
        summary = Muted(ui.Label("", 15));
        path = Muted(ui.Label("", 15));
        ArgonUI.Tooltip(ui.Button(Tr("TUF_OPEN_FOLDER", "Open levels folder"), () => ArgonFiles.Reveal(service.ActiveRootPath)),
            Tr("DESC_TUF_OPEN_FOLDER", "Opens the folder where downloaded TUF levels are saved."));
        UnityEngine.UI.Button pick = ui.Button(Tr("TUF_PICK_FOLDER", "Change levels folder"), () =>
            ArgonFiles.PickFolder(service.ActiveRootPath, Tr("TUF_PICK_FOLDER_TITLE", "Choose a levels folder"), picked => {
                if(picked == null) return;
                ClearNotice();
                if(!service.SetCustomLevelsRoot(picked, out string reason)) {
                    MainCore.Log.Wrn($"[TUF] rejected levels folder '{picked}': {reason}");
                    ShowNotice(RejectionMessage(reason));
                }
                Refresh();
            }));
        ArgonUI.Tooltip(pick, Tr("DESC_TUF_PICK_FOLDER",
            "Choose an empty folder — on a roomier drive, for instance — to keep downloaded levels in. Levels you already have are moved there."));
        ArgonUI.Tooltip(ui.Button(Tr("TUF_RESET_FOLDER", "Use the default folder"), () => {
            ClearNotice();
            if(!service.ClearCustomLevelsRoot(out string reason)) ShowNotice(RejectionMessage(reason));
            Refresh();
        }), Tr("DESC_TUF_RESET_FOLDER", "Moves levels back into the default folder."));
        if(TufHelperLiteLink.Installed) {
            linkedNote = ui.Label(Tr("TUF_LIBRARY_LINK_OVERRIDE",
                "The TUFHelperLite link above is on, so levels install there and this folder is not in use."), 15);
            linkedNote.color = new Color(1f, 0.72f, 0.35f, 0.9f);
        }
        status = Muted(ui.Label("", 15));
        notice = ui.Label("", 15);
        notice.color = new Color(1f, 0.55f, 0.55f, 0.95f);
        service.Changed += Refresh;
        Refresh();
    }

    public void Detach() => service.Changed -= Refresh;

    private static TextMeshProUGUI Muted(TextMeshProUGUI t) {
        t.color = new Color(1, 1, 1, 0.55f);
        return t;
    }

    private void Refresh() {
        if(!summary) return;
        summary.text = string.Format(Tr("TUF_LIBRARY_SUMMARY", "{0} level(s) · {1}"),
            service.InstalledCount, TufDiskSpace.Describe(service.LibraryBytes));
        path.text = service.ActiveRootPath;
        if(linkedNote) linkedNote!.gameObject.SetActive(service.LinkTufHelperLite && !string.IsNullOrEmpty(service.CustomLevelsRoot));
        status.text = service.MoveState switch {
            TufMoveState.Moving => string.Format(Tr("TUF_MOVE_PROGRESS", "Moving levels… {0}/{1}"), service.MoveDone, service.MoveTotal),
            TufMoveState.Failed => service.MoveError,
            TufMoveState.Done when service.MoveTotal > 0 =>
                string.Format(Tr("TUF_MOVE_DONE", "Moved {0} level(s) to the new folder."), service.MoveTotal),
            _ => "",
        };
    }

    private void ShowNotice(string text) {
        if(notice) notice.text = text;
    }

    private void ClearNotice() => ShowNotice("");

    private static string RejectionMessage(string reason) => reason switch {
        "linked" => Tr("TUF_FOLDER_LINKED", "Turn off the TUFHelperLite link first — while it is on, that mod's folder is where levels install."),
        "not-empty" => Tr("TUF_FOLDER_NOT_EMPTY", "Pick an empty folder. Everything inside the levels folder is managed, so a folder that already holds your files can't be used."),
        "volume-root" => Tr("TUF_FOLDER_VOLUME_ROOT", "Pick a folder on the drive, not the drive itself."),
        "nested" => Tr("TUF_FOLDER_NESTED", "That folder is inside the default levels folder."),
        "busy" => Tr("TUF_FOLDER_BUSY", "Levels are still moving; wait for that to finish."),
        "symlink" => Tr("TUF_FOLDER_SYMLINK", "That folder is a link. Pick a real folder."),
        _ => Tr("TUF_FOLDER_UNUSABLE", "That folder cannot be used for levels."),
    };
}
