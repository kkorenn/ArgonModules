using System.Text;
using Argon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Quartz.Features.Tuf.TufCards;

namespace Quartz.Features.Tuf;

/// <summary>The "Packs" sub-tab: a pack list, or one pack's levels (folders expand inline).</summary>
internal sealed class TufPacksView {
    private readonly TufPackService service;
    private readonly TabBuilder list;
    private readonly ScrollRect? scroll;
    private readonly TufCardHooks hooks;
    private readonly Dictionary<int, TextMeshProUGUI> actionLabels = new();
    private readonly HashSet<long> expandedFolders = new();
    private readonly TufPreviews previews = new();
    private string? expandedPackId;
    private string? signature;
    private int gridColumns = 2;
    private bool Grid => TufService.Instance?.GridView == true;
    private int ComputeColumns() => Columns(list.Content.rect.width, gridColumns);

    public TufPacksView(TabBuilder ui, TufPackService service) {
        this.service = service;
        hooks = new TufCardHooks {
            Act = service.Act,
            LaunchChart = service.LaunchChart,
            IsBusy = () => service.IsBusy,
            IsLaunching = () => service.IsLaunching,
            QueuePosition = service.QueuePosition,
            Previews = previews,
            ShowPreviews = () => TufService.Instance?.ShowPreviews ?? true,
        };
        ui.Label(Tr("TUF_PACKS_TAGLINE", "Curated collections of TUF levels."), 15).color = ArgonKit.Dim;
        TMP_InputField search = ui.Input(Tr("TUF_PACK_SEARCH_PLACEHOLDER", "Search packs…"), service.SetQuery);
        search.SetTextWithoutNotify(service.Query);
        ui.SortDropdown(new[] { Tr("TUF_SORT_RECENT", "Recent"), Tr("TUF_PACK_SORT_NAME", "Name"), Tr("TUF_PACK_SORT_LEVELS", "Levels") },
            (int)service.Sort, service.Ascending, (i, ascending) => {
                if(i != (int)service.Sort) service.SetSort((TufPackSort)i);
                if(ascending != service.Ascending) service.ToggleAscending();
            });
        list = ui.Group();
        scroll = ui.Content.GetComponentInParent<ScrollRect>(true); // (true): the window may be built while inactive
        service.Changed += Rebuild;
        if(TufService.Instance != null) TufService.Instance.Changed += Rebuild; // grid/preview settings live there
        service.EnsureLoaded();
        Rebuild();
    }

    public void Detach() {
        service.Changed -= Rebuild;
        if(TufService.Instance != null) TufService.Instance.Changed -= Rebuild;
        previews.Dispose();
    }

    public void Tick() {
        previews.Tick();
        if(Grid && list.Content && ComputeColumns() != gridColumns) { // window resized
            signature = null;
            Rebuild();
        }
        if(scroll == null || service.SelectedPack != null || !service.HasMore || service.LoadingMore
           || service.ListState != TufPackListState.Ready) return;
        RectTransform content = scroll.content;
        float max = content.rect.height - scroll.viewport.rect.height;
        if(max <= 0f || content.anchoredPosition.y >= max - 400f) service.LoadMore();
    }

    private void Rebuild() {
        if(list.Content == null) return;
        string next = BuildSignature();
        if(next == signature) {
            foreach(TufLevel level in service.PackLevels)
                if(actionLabels.TryGetValue(level.Id, out TextMeshProUGUI label) && label)
                    label.text = ActionLabel(level, service.QueuePosition);
            return;
        }
        bool wasDetail = signature?.StartsWith("D:") == true;
        signature = next;
        previews.ClearSlots();
        list.Clear();
        actionLabels.Clear();
        if(service.SelectedPack != null) RebuildDetail();
        else RebuildList();
        if(scroll != null && wasDetail != (service.SelectedPack != null)) scroll.verticalNormalizedPosition = 1; // new view starts at the top
    }

    private void RebuildList() {
        if(service.ListState == TufPackListState.Loading) Status(Tr("TUF_PACK_LOADING", "Loading packs…"));
        else if(service.ListState == TufPackListState.Error && service.Packs.Count == 0) {
            Status((service.OfflineError ? Tr("TUF_OFFLINE", "You appear to be offline.") : Tr("TUF_PACK_API_ERROR", "Could not load TUF packs."))
                   + "\n" + service.ListError);
            list.Button(Tr("TUF_RETRY", "Retry"), service.RefreshPacks);
        } else if(service.ListState == TufPackListState.Empty) Status(Tr("TUF_PACK_EMPTY", "No packs matched your search."));
        else {
            if(Grid) {
                gridColumns = ComputeColumns();
                TufCards.GridRows(list, service.Packs, gridColumns, 128, (row, pack) => PackGridCard(row, pack));
            } else foreach(TufPack pack in service.Packs) PackCard(pack);
            if(service.HasMore) {
                if(service.LoadingMore) Status(Tr("TUF_PACK_LOADING", "Loading packs…"));
                else if(service.ListState == TufPackListState.Error) list.Button(Tr("TUF_RETRY", "Retry"), service.LoadMore);
            } else if(service.Packs.Count > 0) Status(Tr("TUF_END", "End of results"));
        }
    }

    private void RebuildDetail() {
        TufPack pack = service.SelectedPack!;
        if(expandedPackId != pack.Id) {
            expandedPackId = pack.Id;
            expandedFolders.Clear();
        }
        BackRow(pack);
        if(service.DetailState == TufPackListState.Loading) Status(Tr("TUF_PACK_LOADING_LEVELS", "Loading pack levels…"));
        else if(service.DetailState == TufPackListState.Error && service.PackLevels.Count == 0) {
            Status(Tr("TUF_PACK_LEVELS_ERROR", "Could not load this pack.") + "\n" + service.DetailError);
            list.Button(Tr("TUF_RETRY", "Retry"), service.RetryPackLevels);
        } else if(service.DetailState == TufPackListState.Empty) Status(Tr("TUF_PACK_NO_LEVELS", "This pack has no playable levels."));
        else {
            list.SortDropdown(new[] { Tr("TUF_PACK_SORT_ORDER", "Pack order"), Tr("TUF_SORT_DIFFICULTY", "Difficulty"), Tr("TUF_SORT_CLEARS", "Clears") },
                (int)service.LevelSort, service.LevelAscending, (i, ascending) => {
                    if(i != (int)service.LevelSort) service.SetLevelSort((TufPackLevelSort)i);
                    if(ascending != service.LevelAscending) service.ToggleLevelAscending();
                });
            RenderItems(list, service.PackItems, 0);
        }
    }

    private void RenderItems(TabBuilder parent, IReadOnlyList<TufPackItem> items, int depth) {
        gridColumns = ComputeColumns();
        List<TufLevel> run = []; // consecutive levels between folders, laid out as one grid block
        void FlushRun() {
            if(run.Count == 0) return;
            TufCards.GridRows(parent, run.ToList(), gridColumns, GridCardHeight,
                (row, level) => actionLabels[level.Id] = TufCards.AddGrid(row, level, hooks),
                level => { if(level.State == TufItemState.ChooseChart) TufCards.AddChartChooser(parent, level, hooks); });
            run.Clear();
        }
        foreach(TufPackItem item in SortItems(items)) {
            if(Grid && !item.IsFolder && item.Level != null) {
                run.Add(item.Level);
                continue;
            }
            FlushRun();
            if(item.IsFolder) {
                FolderRow(parent, item);
                if(!expandedFolders.Contains(item.Key)) continue;
                TabBuilder children = parent.Group();
                children.Content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(26, 0, 0, 0); // indent one level
                RenderItems(children, item.Children, depth + 1);
            } else if(item.Level != null) {
                actionLabels[item.Level.Id] = TufCards.Add(parent, item.Level, hooks);
                if(item.Level.State == TufItemState.ChooseChart) TufCards.AddChartChooser(parent, item.Level, hooks);
            }
        }
        FlushRun();
    }

    // Folders keep their place; only the levels between them are reordered.
    private IReadOnlyList<TufPackItem> SortItems(IReadOnlyList<TufPackItem> items) {
        if(service.LevelSort == TufPackLevelSort.PackOrder) return items;
        List<TufPackItem> result = [.. items];
        List<int> slots = [];
        List<TufPackItem> levels = [];
        for(int i = 0; i < result.Count; i++) {
            if(result[i].IsFolder) continue;
            slots.Add(i);
            levels.Add(result[i]);
        }
        IEnumerable<TufPackItem> sorted = (service.LevelSort, service.LevelAscending) switch {
            (TufPackLevelSort.Difficulty, true) => levels.OrderBy(i => i.Level?.DifficultyRank ?? 0),
            (TufPackLevelSort.Difficulty, false) => levels.OrderByDescending(i => i.Level?.DifficultyRank ?? 0),
            (TufPackLevelSort.Clears, true) => levels.OrderBy(i => i.Level?.Clears ?? 0),
            _ => levels.OrderByDescending(i => i.Level?.Clears ?? 0),
        };
        int slot = 0;
        foreach(TufPackItem item in sorted) result[slots[slot++]] = item;
        return result;
    }

    private void PackCard(TufPack pack) {
        RectTransform row = list.Row(82);
        Image bg = ArgonKit.Box(row, new Color(1, 1, 1, 0.05f), ArgonRadius.Card);
        ArgonKit.Stretch(bg.rectTransform);
        if(hooks.ShowPreviews()) previews.Attach(bg, "pack-" + pack.Id, TufPreviewSource.ForPack(pack.IconUrl, pack.FirstLevelId));
        ArgonKit.Clickable(bg, () => service.OpenPack(pack), hover: new Color(1, 1, 1, 0.1f), scale: 0);
        Text(bg.transform, pack.Name, 21, -12, -42).fontStyle = FontStyles.Bold;
        string preview = pack.Preview.Count > 0 ? "  ·  " + string.Join(", ", pack.Preview) : "";
        Text(bg.transform, string.Format(Tr("TUF_PACK_LEVEL_COUNT", "{0} levels"), pack.LevelCount)
                           + $"  ·  {pack.Owner}  ·  ♥ {pack.Favorites:N0}" + preview, 14, -46, -70).color = new Color(1, 1, 1, 0.46f);
    }

    private void PackGridCard(RectTransform row, TufPack pack) {
        Image bg = ArgonKit.Box(row, new Color(1, 1, 1, 0.05f), ArgonRadius.Card);
        LayoutElement size = ArgonKit.Size(bg);
        size.minWidth = 0;
        size.flexibleWidth = 1;
        if(hooks.ShowPreviews()) previews.Attach(bg, "pack-" + pack.Id, TufPreviewSource.ForPack(pack.IconUrl, pack.FirstLevelId));
        ArgonKit.Clickable(bg, () => service.OpenPack(pack), hover: new Color(1, 1, 1, 0.1f), scale: 0);
        TextMeshProUGUI name = ArgonKit.Text(bg.transform, pack.Name, 19);
        ArgonKit.Place(name.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(18, -62), new Vector2(-16, -12));
        name.fontStyle = FontStyles.Bold;
        name.overflowMode = TextOverflowModes.Ellipsis; // wraps to two lines, then "…"
        TextMeshProUGUI meta = ArgonKit.Text(bg.transform, string.Format(Tr("TUF_PACK_LEVEL_COUNT", "{0} levels"), pack.LevelCount)
                                                           + $"  ·  ♥ {pack.Favorites:N0}\n{pack.Owner}", 14);
        ArgonKit.Place(meta.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(18, 12), new Vector2(-16, 56));
        meta.color = new Color(1, 1, 1, 0.5f);
        meta.alignment = TextAlignmentOptions.BottomLeft;
        meta.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void BackRow(TufPack pack) {
        RectTransform row = list.Row(52);
        Image bg = ArgonKit.Box(row, ArgonKit.Button, ArgonRadius.Card);
        ArgonKit.Stretch(bg.rectTransform);
        ArgonKit.Clickable(bg, service.ClosePack, scale: 0);
        TextMeshProUGUI name = Text(bg.transform, "‹  " + pack.Name, 19, -12, -40);
        name.fontStyle = FontStyles.Bold;
        TextMeshProUGUI count = ArgonKit.Text(bg.transform, string.Format(Tr("TUF_PACK_LEVEL_COUNT", "{0} levels"), pack.LevelCount),
            15, TextAlignmentOptions.Right);
        ArgonKit.Stretch(count.rectTransform);
        count.rectTransform.offsetMax = new Vector2(-20, 0);
        count.color = new Color(1, 1, 1, 0.46f);
    }

    private void FolderRow(TabBuilder parent, TufPackItem folder) {
        bool expanded = expandedFolders.Contains(folder.Key);
        RectTransform row = parent.Row(48);
        Image bg = ArgonKit.Box(row, ArgonKit.Button, ArgonRadius.Card);
        ArgonKit.Stretch(bg.rectTransform);
        ArgonKit.Clickable(bg, () => {
            if(!expandedFolders.Add(folder.Key)) expandedFolders.Remove(folder.Key);
            signature = null;
            Rebuild();
        }, scale: 0);
        Text(bg.transform, (expanded ? "▾  " : "▸  ") + folder.Name, 18, -10, -38).fontStyle = FontStyles.Bold;
        TextMeshProUGUI count = ArgonKit.Text(bg.transform, string.Format(Tr("TUF_PACK_LEVEL_COUNT", "{0} levels"), folder.LevelCount),
            14, TextAlignmentOptions.Right);
        ArgonKit.Stretch(count.rectTransform);
        count.rectTransform.offsetMax = new Vector2(-18, 0);
        count.color = new Color(1, 1, 1, 0.46f);
    }

    private static TextMeshProUGUI Text(Transform card, string text, float size, float top, float bottom) {
        TextMeshProUGUI t = ArgonKit.Text(card, text, size);
        ArgonKit.Place(t.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(20, bottom), new Vector2(-140, top));
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    private void Status(string text) {
        TextMeshProUGUI label = list.Label(text, 16);
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1, 1, 1, 0.5f);
        label.margin = new Vector4(0, 12, 0, 12);
    }

    private string BuildSignature() {
        StringBuilder sb = new();
        sb.Append(service.OfflineError ? 'O' : 'o').Append(hooks.ShowPreviews() ? 'P' : 'p').Append(Grid ? "G" + ComputeColumns() : "L");
        if(service.SelectedPack != null) {
            sb.Append("D:").Append(service.SelectedPack.Id).Append('|').Append((int)service.DetailState).Append('|')
                .Append(service.IsBusy ? '1' : '0').Append(service.IsLaunching ? '1' : '0').Append(service.QueueCount).Append('|')
                .Append((int)service.LevelSort).Append(service.LevelAscending ? '1' : '0').Append('|')
                .Append(string.Join(",", expandedFolders)).Append('|');
            foreach(TufLevel level in service.PackLevels)
                sb.Append(level.Id).Append(':').Append((int)level.State).Append(level.InstallFolder == null ? '-' : '+')
                    .Append(string.IsNullOrEmpty(level.Error) ? '-' : '!').Append('#').Append(level.Charts?.Count ?? 0).Append(',');
        } else {
            sb.Append("L:").Append((int)service.ListState).Append('|').Append(service.HasMore ? '1' : '0')
                .Append(service.LoadingMore ? '1' : '0').Append('|');
            foreach(TufPack pack in service.Packs) sb.Append(pack.Id).Append(',');
        }
        return sb.ToString();
    }
}
