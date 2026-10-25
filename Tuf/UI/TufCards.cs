using Argon.UI;
using Quartz.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Quartz.Features.Tuf;

/// <summary>What a level card can do; the Levels page wires everything, the Packs page the basics.</summary>
internal sealed class TufCardHooks {
    public Action<TufLevel> Act = _ => { };
    public Action<TufLevel, string> LaunchChart = (_, _) => { };
    public Func<bool> IsBusy = () => false;
    public Func<bool> IsLaunching = () => false;
    public Func<int, int> QueuePosition = _ => 0;
    // Library features (Levels page only).
    public Func<int, int>? SnapshotCount;
    public Action<TufLevel>? Delete;
    public Action<TufLevel>? Rollback;
    public Action<TufLevel>? Badge;
    public Func<int, bool>? IsDeleteArmed;
    // Blurred video thumbnails behind cards.
    public TufPreviews? Previews;
    public Func<bool> ShowPreviews = () => false;
}

/// <summary>Level rows: difficulty rail, id + difficulty + status badge, song, credits, and the action buttons.</summary>
internal static class TufCards {
    private const float Height = 92f;
    internal static readonly Color[] PguGradient = {
        new Color32(0, 153, 255, 255), new Color32(0, 255, 136, 255), new Color32(242, 167, 0, 255),
        new Color32(225, 79, 79, 255), new Color32(210, 0, 151, 255), new Color32(45, 29, 65, 255), new Color32(0, 0, 0, 255),
    };
    internal static readonly Color[] QuantumGradient = {
        new Color32(255, 255, 255, 255), new Color32(241, 161, 5, 255), new Color32(235, 123, 41, 255),
        new Color32(227, 85, 74, 255), new Color32(192, 52, 94, 255), new Color32(214, 16, 136, 255),
        new Color32(113, 73, 164, 255), new Color32(63, 32, 103, 255), new Color32(47, 43, 54, 255),
        new Color32(126, 0, 0, 255), new Color32(255, 254, 254, 255),
    };

    internal static string Tr(string key, string fallback) => MainCore.Tr.Get(key, fallback);

    internal const float GridMinWidth = 268f, GridGap = 8f, GridCardHeight = 176f;

    /// <summary>How many grid columns fit in <paramref name="width"/> (cards at least ~268px wide).</summary>
    public static int Columns(float width, int fallback) =>
        width < 1f ? fallback : Mathf.Clamp(Mathf.FloorToInt((width + GridGap) / (GridMinWidth + GridGap)), 1, 6);

    /// <summary>Lays <paramref name="items"/> out <paramref name="columns"/> per row; <paramref name="cell"/> fills one card.</summary>
    public static void GridRows<T>(TabBuilder list, IReadOnlyList<T> items, int columns, float height, Action<RectTransform, T> cell,
        Action<T>? afterRow = null) {
        for(int start = 0; start < items.Count; start += columns) {
            RectTransform row = list.Row(height);
            HorizontalLayoutGroup h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = GridGap;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            int end = Math.Min(start + columns, items.Count);
            for(int i = start; i < end; i++) cell(row, items[i]);
            for(int i = end - start; i < columns; i++) // empty slots keep the last row's cards the same width
                ArgonKit.Size(ArgonKit.Rect(row, "Spacer"), 0).flexibleWidth = 1;
            if(afterRow != null)
                for(int i = start; i < end; i++) afterRow(items[i]);
        }
    }

    /// <summary>Adds a card; returns the action button's label so progress can update it in place.</summary>
    public static TextMeshProUGUI Add(TabBuilder ui, TufLevel level, TufCardHooks hooks) {
        RectTransform row = ui.Row(Height);
        Image bg = Background(row, level, hooks);
        ArgonKit.Stretch(bg.rectTransform);
        Color diffColor = ColorUtility.TryParseHtmlString(level.DifficultyColor, out Color c) ? c : Color.white;

        Image rail = ArgonKit.Box(bg.transform, diffColor, ArgonRadius.Small);
        ArgonKit.Place(rail.rectTransform, Vector2.zero, new Vector2(0, 1), new Vector2(6, 10), new Vector2(10, -10));

        bool installed = IsInstalled(level);
        int snapshots = installed ? hooks.SnapshotCount?.Invoke(level.Id) ?? 0 : 0;
        float right = 12 + 132 + (installed && hooks.Delete != null ? 84 : 0) + (snapshots > 0 && hooks.Rollback != null ? 96 : 0);

        string readable = "#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(diffColor, Color.white, 0.3f));
        TextMeshProUGUI top = Line(bg.transform, $"<color=#ffffff7a>#{level.Id}</color>   <color={readable}><b>{level.Difficulty}</b></color>",
            15, -8, -30, right);
        if(installed) Badge(bg.transform, level, hooks, 22 + Mathf.Ceil(top.GetPreferredValues().x) + 10);

        string song = string.IsNullOrEmpty(level.Song) ? Tr("TUF_UNKNOWN_LEVEL", "Level") + " #" + level.Id : level.Song;
        Line(bg.transform, song, 21, -31, -58, right).fontStyle = FontStyles.Bold;
        Line(bg.transform, Meta(level), 14, -59, -82, right).color = new Color(1, 1, 1, 0.46f);

        // Right side, from the edge inward: action, delete, rollback.
        float x = -12;
        TextMeshProUGUI label = ActionButton(bg.transform, ref x, 132, level, hooks);
        if(installed && hooks.Delete != null) DeleteButton(bg.transform, ref x, level, hooks);
        if(snapshots > 0 && hooks.Rollback != null) RollbackButton(bg.transform, ref x, level, hooks);
        return label;
    }

    /// <summary>Grid card (176 tall) inside a row with a HorizontalLayoutGroup; cards share the row's width.</summary>
    public static TextMeshProUGUI AddGrid(RectTransform gridRow, TufLevel level, TufCardHooks hooks) {
        Image bg = Background(gridRow, level, hooks);
        LayoutElement size = ArgonKit.Size(bg);
        size.minWidth = 0;
        size.flexibleWidth = 1;
        Color diffColor = ColorUtility.TryParseHtmlString(level.DifficultyColor, out Color c) ? c : Color.white;
        Image rail = ArgonKit.Box(bg.transform, diffColor, ArgonRadius.Small);
        ArgonKit.Place(rail.rectTransform, Vector2.zero, new Vector2(0, 1), new Vector2(6, 10), new Vector2(10, -10));

        bool installed = IsInstalled(level);
        string readable = "#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(diffColor, Color.white, 0.3f));
        TextMeshProUGUI top = Line(bg.transform, $"<color=#ffffff7a>#{level.Id}</color>   <color={readable}><b>{level.Difficulty}</b></color>",
            15, -10, -32, 16);
        if(installed) Badge(bg.transform, level, hooks, 22 + Mathf.Ceil(top.GetPreferredValues().x) + 10, -31, -11);
        string song = string.IsNullOrEmpty(level.Song) ? Tr("TUF_UNKNOWN_LEVEL", "Level") + " #" + level.Id : level.Song;
        Line(bg.transform, song, 20, -38, -64, 16).fontStyle = FontStyles.Bold;
        bool known = !string.IsNullOrEmpty(level.Artist) || !string.IsNullOrEmpty(level.Creator);
        Line(bg.transform, known ? $"{level.Artist}  ·  {level.Creator}" : Tr("TUF_INSTALLED_UNKNOWN", "Downloaded before level details were tracked."),
            14, -66, -86, 16).color = new Color(1, 1, 1, 0.46f);
        if(known)
            Line(bg.transform, $"✓ {level.Clears:N0}    ♥ {level.Likes:N0}", 14, -88, -108, 16).color = new Color(1, 1, 1, 0.46f);

        // Bottom: small buttons on the right, the action button takes the rest.
        float x = -12;
        int snapshots = installed ? hooks.SnapshotCount?.Invoke(level.Id) ?? 0 : 0;
        if(installed && hooks.Delete != null) DeleteButton(bg.transform, ref x, level, hooks, bottom: true);
        if(snapshots > 0 && hooks.Rollback != null) RollbackButton(bg.transform, ref x, level, hooks, bottom: true);
        return ActionButton(bg.transform, ref x, 0, level, hooks, bottom: true);
    }

    private static Image Background(RectTransform parent, TufLevel level, TufCardHooks hooks) {
        Image bg = ArgonKit.Box(parent, new Color(1f, 1f, 1f, 0.05f), ArgonRadius.Card);
        if(hooks.Previews != null && hooks.ShowPreviews()) hooks.Previews.Attach(bg, level.Id.ToString(), TufPreviewSource.Video(level.VideoLink));
        return bg;
    }

    private static TextMeshProUGUI ActionButton(Transform card, ref float x, float width, TufLevel level, TufCardHooks hooks, bool bottom = false) {
        TextMeshProUGUI label = Button(card, ref x, width, ActionLabel(level, hooks.QueuePosition),
            ActionEnabled(level, hooks), primary: true, () => hooks.Act(level), bottom: bottom);
        Component button = label.transform.parent;
        if(!string.IsNullOrWhiteSpace(level.Error))
            ArgonUI.Tooltip(button, level.Error.Length > 900 ? level.Error.Substring(0, 900) + "…" : level.Error);
        else if(level.State == TufItemState.Queued)
            ArgonUI.Tooltip(button, Tr("DESC_TUF_QUEUED", "Waiting for the current download to finish. Click to remove it from the queue."));
        return label;
    }

    private static void DeleteButton(Transform card, ref float x, TufLevel level, TufCardHooks hooks, bool bottom = false) {
        bool armed = hooks.IsDeleteArmed?.Invoke(level.Id) == true;
        TextMeshProUGUI label = Button(card, ref x, 76, armed ? Tr("TUF_DELETE_CONFIRM", "Sure?") : Tr("TUF_DELETE", "Delete"),
            !hooks.IsBusy(), primary: false, () => hooks.Delete!(level),
            armed ? new Color(0.86f, 0.31f, 0.33f, 0.92f) : new Color(0.86f, 0.31f, 0.33f, 0.22f), bottom);
        ArgonUI.Tooltip(label.transform.parent, Tr("DESC_TUF_DELETE",
            "Delete this level from your library. Click it twice to confirm; the level can be downloaded again."));
    }

    private static void RollbackButton(Transform card, ref float x, TufLevel level, TufCardHooks hooks, bool bottom = false) {
        TextMeshProUGUI label = Button(card, ref x, 88, Tr("TUF_ROLLBACK", "Rollback"), !hooks.IsBusy(), primary: false,
            () => hooks.Rollback!(level), bottom: bottom);
        ArgonUI.Tooltip(label.transform.parent, Tr("DESC_TUF_ROLLBACK",
            "Go back to a version of this level you had before an update. The last few copies are kept in the rollback folder inside your levels folder."));
    }

    /// <summary>One row per chart when a download contains several; picking one loads it.</summary>
    public static void AddChartChooser(TabBuilder ui, TufLevel level, TufCardHooks hooks) {
        if(level?.Charts == null) return;
        foreach(string chart in level.Charts) {
            string path = chart;
            ui.Button("›  " + ChartDisplayName(level, chart), () => hooks.LaunchChart(level, path));
        }
    }

    public static bool IsInstalled(TufLevel level) =>
        level.InstallFolder != null
        && level.State is not TufItemState.Downloading and not TufItemState.Extracting and not TufItemState.Loading;

    public static string ActionLabel(TufLevel level, Func<int, int> queuePosition) => level.State switch {
        TufItemState.Downloading => level.Progress < 0
            ? Tr("TUF_DOWNLOADING", "Downloading…")
            : string.Format(Tr("TUF_DOWNLOADING_PROGRESS", "Downloading {0}%"), Mathf.Clamp((int)(level.Progress * 100f), 0, 100)),
        TufItemState.Extracting => Tr("TUF_EXTRACTING", "Extracting…"),
        TufItemState.Loading => Tr("TUF_LOADING_LEVEL", "Loading…"),
        TufItemState.Load => Tr("TUF_LOAD", "Load"),
        TufItemState.Retry => Tr("TUF_RETRY", "Retry"),
        TufItemState.Unavailable => TufMainLevel.Resolve(level, out _) switch {
            TufMainLevel.TufMainAction.Play => Tr("TUF_PLAY", "Play"),
            TufMainLevel.TufMainAction.BuyDlc => Tr("TUF_BUY_DLC", "Buy DLC"),
            _ => Tr("TUF_UNAVAILABLE", "Unavailable"),
        },
        TufItemState.ChooseChart => Tr("TUF_CANCEL", "Cancel"),
        TufItemState.Queued => string.Format(Tr("TUF_QUEUED", "Queued #{0}"), queuePosition(level.Id)),
        _ => Tr("TUF_DOWNLOAD", "Download"),
    };

    private static bool ActionEnabled(TufLevel level, TufCardHooks hooks) {
        bool actionable = level.State is not TufItemState.Unavailable and not TufItemState.Downloading
                and not TufItemState.Extracting and not TufItemState.Loading
            || (level.State == TufItemState.Unavailable && TufMainLevel.Resolve(level, out _) != TufMainLevel.TufMainAction.None);
        return actionable && !hooks.IsLaunching();
    }

    private static string Meta(TufLevel level) {
        string size = level.SizeBytes > 0 ? "  ·  " + TufDiskSpace.Describe(level.SizeBytes) : "";
        string error = string.IsNullOrWhiteSpace(level.Error) ? "" : "  ·  <color=#ff8080>" + level.Error + "</color>";
        if(string.IsNullOrEmpty(level.Artist) && string.IsNullOrEmpty(level.Creator))
            return Tr("TUF_INSTALLED_UNKNOWN", "Downloaded before level details were tracked.") + size + error;
        return $"{level.Artist}  ·  {level.Creator}  ·  ✓ {level.Clears:N0}  ♥ {level.Likes:N0}{size}{error}";
    }

    private static TextMeshProUGUI Line(Transform card, string text, float size, float top, float bottom, float right) {
        TextMeshProUGUI t = ArgonKit.Text(card, text, size);
        ArgonKit.Place(t.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(22, bottom), new Vector2(-right, top));
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    private static void Badge(Transform card, TufLevel level, TufCardHooks hooks, float x, float bottom = -29, float top = -9) {
        TufUpdateState state = level.UpdateState;
        string text = state switch {
            TufUpdateState.Checking => Tr("TUF_UPDATE_CHECKING", "Checking…"),
            TufUpdateState.Available => Tr("TUF_UPDATE_AVAILABLE", "Update"),
            TufUpdateState.UpToDate => Tr("TUF_UPDATE_CURRENT", "Up to date"),
            TufUpdateState.Updating => Tr("TUF_UPDATE_RUNNING", "Updating…"),
            _ => Tr("TUF_INSTALLED", "Installed"),
        };
        Color fill = state switch {
            TufUpdateState.Available => new Color(0.29f, 0.56f, 0.95f, 0.32f),
            TufUpdateState.Checking or TufUpdateState.Updating => new Color(1, 1, 1, 0.12f),
            _ => new Color(0.38f, 0.78f, 0.52f, 0.22f),
        };
        Image badge = ArgonKit.Box(card, fill, ArgonRadius.Inner);
        TextMeshProUGUI label = ArgonKit.Text(badge.transform, text, 13, TextAlignmentOptions.Center);
        ArgonKit.Stretch(label.rectTransform);
        label.color = state == TufUpdateState.Available ? new Color(0.72f, 0.86f, 1f) : new Color(0.62f, 0.92f, 0.72f);
        ArgonKit.Place(badge.rectTransform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(x, bottom), new Vector2(x + Mathf.Ceil(label.GetPreferredValues(text).x) + 20, top));
        bool clickable = hooks.Badge != null && !hooks.IsLaunching()
            && state is not TufUpdateState.Checking and not TufUpdateState.Updating;
        if(clickable) {
            ArgonKit.Clickable(badge, () => hooks.Badge!(level));
            ArgonUI.Tooltip(badge, Tr("DESC_TUF_UPDATE_BADGE",
                "Click to ask TUF whether this level has been re-uploaded since you downloaded it. When an update is waiting, clicking downloads it over the copy you have."));
        } else badge.raycastTarget = false;
    }

    // width 0 = stretch from the card's left side (grid action button). bottom = bottom row instead of centered.
    private static TextMeshProUGUI Button(Transform card, ref float x, float width, string text, bool enabled, bool primary,
        Action onClick, Color? tint = null, bool bottom = false) {
        Color color = tint ?? (primary ? new Color(ArgonKit.Accent.r, ArgonKit.Accent.g, ArgonKit.Accent.b, 0.85f) : ArgonKit.Button);
        if(!enabled) color = new Color(1, 1, 1, 0.04f);
        Image button = ArgonKit.Box(card, color, ArgonRadius.Control);
        if(bottom)
            ArgonKit.Place(button.rectTransform, width <= 0 ? Vector2.zero : new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(width <= 0 ? 20 : x - width, 14), new Vector2(x, 50));
        else
            ArgonKit.Place(button.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(x - width, -19), new Vector2(x, 19));
        x -= width + 8;
        TextMeshProUGUI label = ArgonKit.Text(button.transform, text, 15, TextAlignmentOptions.Center);
        ArgonKit.Stretch(label.rectTransform, 4);
        label.fontStyle = FontStyles.Bold;
        label.color = new Color(1, 1, 1, enabled ? 1f : 0.45f);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        // Shrink to fit long states ("Downloading 100%") instead of cutting them off.
        label.enableAutoSizing = true;
        label.fontSizeMin = 11;
        label.fontSizeMax = 15;
        label.overflowMode = TextOverflowModes.Ellipsis;
        if(enabled) ArgonKit.Clickable(button, onClick);
        else button.raycastTarget = false;
        return label;
    }

    private static string ChartDisplayName(TufLevel level, string chart) {
        try {
            return string.IsNullOrEmpty(level.ChartsRoot) ? Path.GetFileName(chart) : Path.GetRelativePath(level.ChartsRoot, chart);
        } catch(Exception e) {
            Diag.Ignore(e);
            return Path.GetFileName(chart);
        }
    }
}
