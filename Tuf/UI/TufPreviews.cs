using UnityEngine;
using UnityEngine.UI;

namespace Quartz.Features.Tuf;

/// <summary>
/// Blurred video thumbnails behind cards (ported from Quartz's TufPreviewGroup). Thumbnails come from
/// TufPreviewCache (Logic/), which downloads and blurs them in the background; slots fade in once ready.
/// </summary>
internal sealed class TufPreviews {
    private readonly Dictionary<string, Slot> slots = new(StringComparer.Ordinal);
    private volatile bool dirty;

    public TufPreviews() => TufPreviewCache.Changed += OnReady;

    private void OnReady() => dirty = true; // may come from a worker thread; applied in Tick

    /// <summary>Adds a hidden cover image to <paramref name="card"/> (call right after creating the card's background,
    /// so it sits behind everything else). The card's image becomes the clipping mask.</summary>
    public void Attach(Image card, string key, TufPreviewSource source) {
        if(!source.HasThumbnail) return;
        card.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        RectTransform host = Fill("Preview", card.transform);
        CanvasGroup group = host.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        TufCoverImage image = host.gameObject.AddComponent<TufCoverImage>();
        image.raycastTarget = false;
        Image scrim = Fill("Scrim", host).gameObject.AddComponent<Image>();
        scrim.color = new Color(0f, 0f, 0f, 0.5f); // keeps the text readable over any thumbnail
        scrim.raycastTarget = false;
        slots[key] = new Slot { Image = image, Group = group };
        if(TufPreviewCache.TryGet(key, out Texture2D? ready) && ready != null) Apply(key, ready);
        else TufPreviewCache.Request(key, source);
    }

    public void Tick() {
        if(!dirty) return;
        dirty = false;
        foreach(KeyValuePair<string, Slot> pair in slots) {
            if(pair.Value.Applied || pair.Value.Image == null) continue;
            if(TufPreviewCache.TryGet(pair.Key, out Texture2D? tex) && tex != null) Apply(pair.Key, tex);
        }
    }

    private void Apply(string key, Texture2D texture) {
        if(!slots.TryGetValue(key, out Slot slot) || slot.Applied || slot.Image == null || slot.Group == null) return;
        slot.Image.SetCover(texture);
        slot.Applied = true;
        slot.Group.gameObject.AddComponent<TufFadeIn>();
    }

    /// <summary>Forget slots before the cards are rebuilt (their objects are being destroyed).</summary>
    public void ClearSlots() => slots.Clear();

    public void Dispose() {
        TufPreviewCache.Changed -= OnReady;
        slots.Clear();
    }

    private static RectTransform Fill(string name, Transform parent) {
        GameObject obj = new(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private sealed class Slot {
        public TufCoverImage Image;
        public CanvasGroup Group;
        public bool Applied;
    }
}

/// <summary>Raw image that crops its texture to fill the rect (like CSS background-size: cover).</summary>
internal sealed class TufCoverImage : RawImage {
    private Texture cover;

    public void SetCover(Texture texture) {
        cover = texture;
        this.texture = texture;
        Recompute();
    }

    protected override void OnRectTransformDimensionsChange() {
        base.OnRectTransformDimensionsChange();
        Recompute();
    }

    private void Recompute() {
        if(cover == null) return;
        Rect r = rectTransform.rect;
        if(r.width < 1f || r.height < 1f) return;
        float tw = Mathf.Max(1, cover.width), th = Mathf.Max(1, cover.height);
        float ra = r.width / r.height, ta = tw / th;
        uvRect = ta > ra
            ? new Rect((1f - ra / ta) * 0.5f, 0f, ra / ta, 1f)
            : new Rect(0f, (1f - ta / ra) * 0.5f, 1f, ta / ra);
    }
}

/// <summary>Fades a canvas group in over ~0.3 s, then removes itself.</summary>
internal sealed class TufFadeIn : MonoBehaviour {
    private CanvasGroup group;

    private void Awake() => group = GetComponent<CanvasGroup>();

    private void Update() {
        group.alpha = Mathf.MoveTowards(group.alpha, 1f, Time.unscaledDeltaTime / 0.28f);
        if(group.alpha >= 1f) Destroy(this);
    }
}
