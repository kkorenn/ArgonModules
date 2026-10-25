using Argon.UI;
using Quartz.Core;

namespace Quartz.Features.Tuf;

/// <summary>"Save editor changes first?" prompt; same API as Quartz's, drawn with Argon's dialog.</summary>
internal static class TufUnsavedDialog {
    internal const int Pending = -1;
    internal const int Save = 0;
    internal const int Discard = 1;
    internal const int Cancel = 2;
    private static ArgonDialog dialog;
    internal static bool IsOpen => dialog?.IsOpen == true;

    internal static void Show(Action<int> onChoice) {
        Close();
        dialog = ArgonUI.Dialog(
            MainCore.Tr.Get("TUF_UNSAVED_TITLE", "Unsaved editor changes"),
            MainCore.Tr.Get("TUF_UNSAVED_BODY",
                "The level open in the editor has changes you have not saved. Save them before loading this TUF level?"),
            dismissable: false,
            buttons: new[] {
                new DialogButton(MainCore.Tr.Get("TUF_UNSAVED_CANCEL", "Cancel"), () => onChoice(Cancel)),
                new DialogButton(MainCore.Tr.Get("TUF_UNSAVED_DISCARD", "Discard"), () => onChoice(Discard)),
                new DialogButton(MainCore.Tr.Get("TUF_UNSAVED_SAVE", "Save"), () => onChoice(Save), primary: true),
            });
    }

    internal static void Close() {
        dialog?.Close();
        dialog = null;
    }
}

/// <summary>Pick an earlier snapshot of a level to restore; same API as Quartz's.</summary>
internal static class TufRollbackDialog {
    private const int MaxRows = 6;
    private static ArgonDialog dialog;
    internal static bool IsOpen => dialog?.IsOpen == true;

    internal static void Show(string title, IReadOnlyList<TufSnapshot> snapshots, Action<long> onRestore) {
        Close();
        if(snapshots == null || snapshots.Count == 0) return;
        ArgonDialog opened = null;
        opened = dialog = ArgonUI.Dialog(
            MainCore.Tr.Get("TUF_ROLLBACK_TITLE", "Roll back to an earlier version"),
            title,
            ui => {
                foreach(TufSnapshot snapshot in snapshots.Take(MaxRows)) {
                    long stamp = snapshot.Stamp;
                    ui.Button(Describe(snapshot), () => {
                        opened?.Close();
                        onRestore(stamp);
                    });
                }
            },
            width: 620f,
            buttons: new[] { new DialogButton(MainCore.Tr.Get("TUF_ROLLBACK_CANCEL", "Cancel")) });
    }

    internal static void Close() {
        dialog?.Close();
        dialog = null;
    }

    internal static string Describe(TufSnapshot snapshot) {
        string when = snapshot.LocalTime.ToString("yyyy-MM-dd HH:mm");
        string size = snapshot.SizeBytes > 0 ? "  ·  " + TufDiskSpace.Describe(snapshot.SizeBytes) : "";
        return when + size + "  ·  " + MainCore.Tr.Get("TUF_ROLLBACK_UNIX", "unix") + " " + snapshot.Stamp;
    }
}
