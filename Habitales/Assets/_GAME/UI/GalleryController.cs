using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// GalleryController — U4 Screenshot Gallery (UI Architecture §5.5).
// Lists *.png files under ScreenshotService.GalleryPath, newest-first,
// and populates a scrollable grid with thumbnail Image items.
//
// WIRING (human steps):
//   1. Create a Gallery panel prefab:
//        • Root RectTransform "Gallery" (sits inside the UIManager canvas, can be toggled).
//        • Add a Canvas + CanvasGroup on the root (SetVisible uses the CanvasGroup alpha
//          so it can fade without destroying layout state — or use SetActive if preferred).
//          Alternatively, just a plain GameObject root is fine; SetVisible calls SetActive.
//        • Inside the root: a ScrollRect → Viewport → Content (RectTransform with a
//          GridLayoutGroup or VerticalLayoutGroup).
//   2. Create a thumbnail-item prefab:
//        • Root RectTransform with an Image component (the thumbnail fills it via Preserve Aspect).
//        • Size it to match your GridLayoutGroup cell size.
//   3. Add GalleryController component to the Gallery root GameObject.
//   4. In the Inspector wire:
//        • Gallery Root      → the root RectTransform of the gallery panel
//        • Content           → the ScrollRect's Content RectTransform (receives instantiated items)
//        • Thumbnail Prefab  → the thumbnail-item prefab (step 2)
//   5. Add GalleryController to UIManager's serialized subsystems list AND into the typed
//      "gallery" slot on UIManager.
//   6. Start with the gallery root deactivated (SetVisible(false) is called in Awake).

namespace Habitales.UI
{
    /// <summary>
    /// UI subsystem that displays a scrollable gallery of PNG screenshots saved by
    /// <see cref="ScreenshotService"/>. Thumbnails are loaded from disk at runtime.
    /// </summary>
    public class GalleryController : MonoBehaviour, IUISubsystem
    {
        // ─── IUISubsystem ─────────────────────────────────────────────────────

        public string SubsystemId => "gallery";

        public bool IsVisible =>
            _galleryRoot != null && _galleryRoot.gameObject.activeSelf;

        public void SetVisible(bool visible)
        {
            if (_galleryRoot == null) return;
            _galleryRoot.gameObject.SetActive(visible);

            // Refresh the grid whenever we become visible so new screenshots appear.
            if (visible)
                Refresh();
        }

        // ─── Serialized refs (Law 3 — loud-fail) ──────────────────────────────

        [Header("References")]
        [Tooltip("Root RectTransform of the gallery panel. SetVisible toggles its GameObject.")]
        [SerializeField] private RectTransform _galleryRoot;

        [Tooltip("Content RectTransform inside the ScrollRect (receives thumbnail items).")]
        [SerializeField] private RectTransform _content;

        [Tooltip("Prefab instantiated per screenshot. Must have an Image component on its root.")]
        [SerializeField] private GameObject _thumbnailPrefab;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            ValidateRefs();

            // Start hidden; hub or a button calls SetVisible(true).
            if (_galleryRoot != null)
                _galleryRoot.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (IsVisible)
                Refresh();
        }

        // ─── Validation (Law 3) ───────────────────────────────────────────────

        private void ValidateRefs()
        {
            bool ok = true;

            if (_galleryRoot == null)
            {
                Debug.LogError($"{name}: _galleryRoot is not wired — assign the gallery panel root in the Inspector.", this);
                ok = false;
            }
            if (_content == null)
            {
                Debug.LogError($"{name}: _content is not wired — assign the ScrollRect Content RectTransform in the Inspector.", this);
                ok = false;
            }
            if (_thumbnailPrefab == null)
            {
                Debug.LogError($"{name}: _thumbnailPrefab is not wired — assign the thumbnail-item prefab in the Inspector.", this);
                ok = false;
            }

            if (!ok) enabled = false;
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Clears the grid and repopulates it from all *.png files under
        /// <see cref="ScreenshotService.GalleryPath"/>, newest file first.
        /// Call this after a new screenshot is taken to update the gallery.
        /// </summary>
        public void Refresh()
        {
            if (!enabled) return;

            ClearGrid();

            string folder = ScreenshotService.GalleryPath;
            if (!Directory.Exists(folder))
            {
                // No screenshots yet — nothing to show.
                return;
            }

            // Enumerate PNGs, sort by last-write descending (newest first).
            var files = Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly)
                                 .Select(f => new FileInfo(f))
                                 .OrderByDescending(fi => fi.LastWriteTime)
                                 .ToArray();

            foreach (var fi in files)
            {
                LoadThumbnail(fi.FullName);
            }
        }

        // ─── Private helpers ──────────────────────────────────────────────────

        private void ClearGrid()
        {
            if (_content == null) return;

            // Destroy all current thumbnail children.
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
        }

        private void LoadThumbnail(string filePath)
        {
            // Read PNG bytes from disk and decode into a Texture2D.
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{name}: Could not read screenshot file '{filePath}' — {ex.Message}", this);
                return;
            }

            // 1×1 placeholder; LoadImage resizes to match the PNG.
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                Debug.LogError($"{name}: Failed to decode PNG '{filePath}'.", this);
                Destroy(tex);
                return;
            }

            // Create a Sprite from the texture.
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f));

            // Instantiate thumbnail item and assign the sprite.
            var item = Instantiate(_thumbnailPrefab, _content);

            var img = item.GetComponent<Image>();
            if (img == null)
            {
                Debug.LogError($"{name}: Thumbnail prefab '{_thumbnailPrefab.name}' has no Image component on its root. " +
                               "Add an Image to the prefab root.", this);
                Destroy(item);
                Destroy(tex);
                return;
            }

            img.sprite = img.preserveAspect
                ? sprite      // honour existing setting
                : sprite;     // set regardless

            img.preserveAspect = true;
            img.sprite          = sprite;

            // Optionally name the item for easier Inspector debugging.
            item.name = Path.GetFileNameWithoutExtension(filePath);
        }
    }
}
