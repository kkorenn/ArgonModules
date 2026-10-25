using System.Text;
using Argon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Quartz.Features.Tuf.TufCards;

namespace Quartz.Features.Tuf;

/// <summary>The "Levels" sub-tab: search + filters on top, a list that rebuilds when the results change.</summary>
internal sealed class TufLevelsPage {
    private const float ArmSeconds = 4f;
    private readonly TufPreviews previews = new();
    private int gridColumns = 2;
    private readonly TufService service;
    private readonly TabBuilder list;
    private readonly ScrollRect? scroll;
    private readonly TufCardHooks hooks;
    private readonly Dictionary<int, TextMeshProUGUI> actionLabels = new();
    private readonly TextMeshProUGUI updatesLabel;
    private readonly Button updatesButton; // kept: GetComponentInParent skips inactive objects (the window builds inactive)
    private string? signature;
    private readonly ArgonRangeSlider quantumRange;
    private int armedDeleteId;
    private float armedUntil;

    public TufLevelsPage(TabBuilder ui, TufService service) {
        this.service = service;
        hooks = new TufCardHooks {
            Act = service.Act,
            LaunchChart = service.LaunchChart,
            IsBusy = () => service.IsBusy,
            IsLaunching = () => service.IsLaunching,
            QueuePosition = service.QueuePosition,
            SnapshotCount = service.SnapshotCount,
            Delete = Delete,
            Rollback = OpenRollback,
            Badge = level => {
                if(level.UpdateState == TufUpdateState.Available) service.UpdateLevel(level);
                else service.CheckUpdate(level);
            },
            IsDeleteArmed = id => armedDeleteId == id,
            Previews = previews,
            ShowPreviews = () => service.ShowPreviews,
        };

        // Row 1: search (wide), refresh, updates.
        TabBuilder top = ui.Horizontal();
        TMP_InputField search = top.Input(Tr("TUF_SEARCH_PLACEHOLDER", "Search levels…"), service.SetQuery);
        search.SetTextWithoutNotify(service.Query);
        ArgonKit.Size(search).flexibleWidth = 1; // the buttons are fit-to-text, search takes the rest
        ArgonUI.Tooltip(search, Tr("TUF_TAGLINE", "Browse community levels, download them, then load them in the editor."));
        FitToText(top.Button(Tr("TUF_REFRESH", "Refresh"), service.Refresh));
        Button updates = top.Button("", () => {
            DisarmDelete();
            if(service.UpdatesAvailable > 0) service.UpdateAll();
            else service.CheckUpdates();
        });
        updatesButton = updates;
        updatesLabel = updates.GetComponentInChildren<TextMeshProUGUI>(true);
        ArgonUI.Tooltip(updates, Tr("DESC_TUF_CHECK_UPDATES",
            "Ask TUF whether any level in your library has been re-uploaded since you downloaded it. One request per level, "
            + "so a large library takes a moment. This is also checked once a day; when updates are waiting, clicking queues all of them."));

        // Row 2: sort key + direction arrow, then the filter pills.
        TabBuilder sort = ui.Horizontal();
        sort.SortDropdown(new[] {
            Tr("TUF_SORT_RECENT", "Recent"), Tr("TUF_SORT_DIFFICULTY", "Difficulty"),
            Tr("TUF_SORT_CLEARS", "Clears"), Tr("TUF_SORT_LIKES", "Likes"),
        }, (int)service.Sort, service.Ascending, (i, ascending) => {
            if(i != (int)service.Sort) service.SetSort((TufSort)i);
            if(ascending != service.Ascending) service.ToggleAscending();
        });
        // Same row: Downloaded, the special difficulties (multi-select dropdown), then quantum on the right.
        ArgonUI.Tooltip(Compact(sort.ToggleChips(new[] { Tr("TUF_INSTALLED_SHORT", "Downloaded") }, new[] { service.ShowInstalled }, (_, _) => {
            DisarmDelete();
            service.ToggleInstalled();
        })), Tr("DESC_TUF_INSTALLED", "Show only the levels you have downloaded, newest first. Works offline."));
        string[] specials = TufDifficultyFilter.SpecialNames.ToArray();
        sort.MultiDropdown("", specials.Select(SpecialLabel).ToArray(), specials.Select(n => service.SelectedDifficulties.Contains(n)).ToArray(),
            (i, _) => service.ToggleSpecialDifficulty(specials[i]), Tr("TUF_SPECIAL", "Special") + ": ");
        sort.Spacer();
        Compact(sort.ToggleChips(new[] { Tr("TUF_QUANTUM", "Quantum") }, new[] { service.QuantumEnabled }, (_, on) => {
            if(on) service.SetQuantumRange(service.QuantumMinIndex, service.QuantumMaxIndex);
            else service.ClearQuantum();
        }));

        // Row 3: P/G/U range. Row 4: quantum range, only while the quantum filter pill is on (slides open/closed).
        ui.RangeSlider(Tr("TUF_PGU", "P / G / U"), TufDifficultyFilter.RankedNames.ToArray(),
            service.MinDifficultyIndex, service.MaxDifficultyIndex, service.SetDifficultyRange, PguGradient);
        quantumRange = ui.RangeSlider(Tr("TUF_QUANTUM", "Quantum"), TufDifficultyFilter.QuantumNames.ToArray(),
            service.QuantumMinIndex, service.QuantumMaxIndex, service.SetQuantumRange, QuantumGradient);
        ArgonKit.Show(quantumRange, service.QuantumEnabled, animate: false);

        list = ui.Group();
        scroll = ui.Content.GetComponentInParent<ScrollRect>(true); // (true): the window may be built while inactive
        service.Changed += Rebuild;
        service.EnsureLoaded();
        Rebuild();
    }

    public void Detach() {
        service.Changed -= Rebuild;
        previews.Dispose();
    }

    /// <summary>Every frame while the window is open: endless scroll and the delete-confirm timeout.</summary>
    public void Tick() {
        previews.Tick();
        if(armedDeleteId != 0 && Time.unscaledTime >= armedUntil) DisarmDelete();
        // Grid columns follow the window width (it can be resized).
        if(service.GridView && list.Content && ComputeColumns() != gridColumns) {
            signature = null;
            Rebuild();
        }
        if(scroll == null || !service.HasMore || service.LoadingMore || service.State != TufListState.Ready) return;
        RectTransform content = scroll.content;
        float max = content.rect.height - scroll.viewport.rect.height;
        if(max <= 0f || content.anchoredPosition.y >= max - 400f) service.LoadMore();
    }

    // Chips keep their own width in a Horizontal row (the spacer takes the leftover).
    private static T Compact<T>(T widget) where T : Component {
        ArgonKit.Size(widget).flexibleWidth = 0;
        return widget;
    }

    private string SpecialLabel(string name) => name switch {
        "Unranked" => Tr("TUF_SPECIAL_UNRANKED", "Unranked"),
        "Censored" => Tr("TUF_SPECIAL_CENSORED", "Censored"),
        "Impossible" => Tr("TUF_SPECIAL_IMPOSSIBLE", "Impossible"),
        _ => name,
    };

    private void Rebuild() {
        if(list.Content == null) return; // window rebuilt; this page is stale until Detach runs
        RefreshControls();
        string next = BuildSignature();
        if(next == signature) {
            // Same rows: only progress text changed (downloads, queue positions).
            foreach(TufLevel level in service.Levels)
                if(actionLabels.TryGetValue(level.Id, out TextMeshProUGUI label) && label)
                    label.text = ActionLabel(level, service.QueuePosition);
            return;
        }
        signature = next;
        previews.ClearSlots();
        list.Clear();
        actionLabels.Clear();

        if(service.State == TufListState.Loading) Status(Tr("TUF_LOADING", "Loading levels…"));
        else if(service.State == TufListState.Error && service.Levels.Count == 0) {
            if(service.OfflineError && !service.ShowInstalled) {
                Status(Tr("TUF_OFFLINE", "You appear to be offline."));
                list.Button(Tr("TUF_OFFLINE_SWITCH", "Show downloaded levels"), service.ShowInstalledLevels);
            } else {
                Status(Tr("TUF_API_ERROR", "Could not load TUF levels.") + "\n" + service.Error);
                list.Button(Tr("TUF_RETRY", "Retry"), service.Refresh);
            }
        } else if(service.State == TufListState.Empty) Status(EmptyMessage());
        else {
            if(service.GridView) AddGrid();
            else
                foreach(TufLevel level in service.Levels) {
                    actionLabels[level.Id] = TufCards.Add(list, level, hooks);
                    if(level.State == TufItemState.ChooseChart) TufCards.AddChartChooser(list, level, hooks);
                }
            if(service.HasMore) {
                if(service.LoadingMore) Status(Tr("TUF_LOADING", "Loading levels…"));
                else if(service.State == TufListState.Error) list.Button(Tr("TUF_RETRY", "Retry"), service.LoadMore);
            } else if(service.Levels.Count > 0)
                Status(service.ShowInstalled ? InstalledFooter() : Tr("TUF_END", "End of results"));
        }
    }

    private int ComputeColumns() => Columns(list.Content.rect.width, gridColumns);

    private void AddGrid() {
        gridColumns = ComputeColumns();
        TufCards.GridRows(list, service.Levels, gridColumns, GridCardHeight,
            (row, level) => actionLabels[level.Id] = TufCards.AddGrid(row, level, hooks),
            level => { if(level.State == TufItemState.ChooseChart) TufCards.AddChartChooser(list, level, hooks); });
    }

    // Button only as wide as its label (+ padding); called again when the label changes.
    private static void FitToText(Button button) {
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        LayoutElement size = ArgonKit.Size(button);
        size.preferredWidth = size.minWidth = Mathf.Ceil(label.GetPreferredValues(label.text).x) + 32;
        size.flexibleWidth = 0;
    }

    private void RefreshControls() {
        if(quantumRange) ArgonKit.Show(quantumRange, service.QuantumEnabled);
        if(updatesLabel)
            updatesLabel.text = service.CheckingUpdates
                ? string.Format(Tr("TUF_CHECKING_UPDATES", "{0}/{1}"), service.UpdateCheckDone, service.UpdateCheckTotal)
                : service.UpdatesAvailable > 0
                    ? string.Format(Tr("TUF_UPDATE_ALL", "Update all ({0})"), service.UpdatesAvailable)
                    : Tr("TUF_CHECK_UPDATES", "Check for updates");
        if(updatesButton) FitToText(updatesButton);
    }

    private void Status(string text) {
        TextMeshProUGUI label = list.Label(text, 16);
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1, 1, 1, 0.5f);
        label.margin = new Vector4(0, 12, 0, 12);
    }

    private string BuildSignature() {
        StringBuilder sb = new();
        sb.Append((int)service.State).Append('|').Append(service.HasMore ? '1' : '0').Append(service.LoadingMore ? '1' : '0')
            .Append(service.IsBusy ? '1' : '0').Append(service.IsLaunching ? '1' : '0').Append(service.QueueCount).Append('|')
            .Append(service.ShowInstalled ? '1' : '0').Append(service.OfflineError ? '1' : '0').Append(service.InfoRevision)
            .Append(service.ShowPreviews ? 'P' : 'p').Append(service.GridView ? "G" + ComputeColumns() : "L")
            .Append('|').Append(armedDeleteId).Append('|');
        foreach(TufLevel level in service.Levels)
            sb.Append(level.Id).Append(':').Append((int)level.State).Append((int)level.UpdateState)
                .Append(service.SnapshotCount(level.Id)).Append(level.InstallFolder == null ? '-' : '+')
                .Append(string.IsNullOrEmpty(level.Error) ? '-' : '!').Append('#').Append(level.Charts?.Count ?? 0).Append(',');
        return sb.ToString();
    }

    private string InstalledFooter() {
        string text = string.Format(Tr("TUF_INSTALLED_COUNT", "{0} level(s) in your library"), service.Levels.Count);
        if(service.LibraryBytes > 0) text += "  ·  " + TufDiskSpace.Describe(service.LibraryBytes);
        if(service.UpdatesAvailable > 0)
            text += "  ·  " + string.Format(Tr("TUF_UPDATES_READY", "{0} update(s) available"), service.UpdatesAvailable);
        return text;
    }

    private string EmptyMessage() {
        if(!service.ShowInstalled) return Tr("TUF_EMPTY", "No levels matched your search.");
        return string.IsNullOrEmpty(service.Query)
            ? Tr("TUF_INSTALLED_EMPTY", "You have not downloaded any levels yet.")
            : Tr("TUF_INSTALLED_NO_MATCH", "No downloaded level matched your search.");
    }

    // Delete takes two clicks: the first arms it ("Sure?") for a few seconds.
    private void Delete(TufLevel level) {
        if(armedDeleteId == level.Id) {
            DisarmDelete();
            service.DeleteInstalled(level);
            return;
        }
        armedDeleteId = level.Id;
        armedUntil = Time.unscaledTime + ArmSeconds;
        Rebuild();
    }

    private void DisarmDelete() {
        if(armedDeleteId == 0) return;
        armedDeleteId = 0;
        Rebuild();
    }

    private void OpenRollback(TufLevel level) {
        DisarmDelete();
        IReadOnlyList<TufSnapshot> snapshots = service.Snapshots(level.Id);
        if(snapshots.Count == 0) {
            service.RefreshSnapshotCounts();
            Rebuild();
            return;
        }
        string title = string.IsNullOrEmpty(level.Song)
            ? Tr("TUF_UNKNOWN_LEVEL", "Level") + " #" + level.Id
            : level.Song + "  ·  #" + level.Id;
        TufRollbackDialog.Show(title, snapshots, stamp => service.RestoreSnapshot(level, stamp));
    }
}
