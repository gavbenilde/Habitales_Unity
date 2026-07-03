using System;
using System.Collections;
using System.IO;
using UnityEngine;

// ScreenshotService — U4 Screenshot + Gallery (UI Architecture §5.5).
// Owns the GalleryPath convention (S2 — one folder, shared by capture and gallery).
// Capture flow: hide all UI → WaitForEndOfFrame → ReadPixels → PNG → restore UI.
//
// WIRING (human steps):
//   1. Add ScreenshotService as a component on any persistent GameObject
//      (e.g. the "UIManager" GameObject, or a dedicated "ScreenshotService" one).
//      If you want it to survive scene loads, call DontDestroyOnLoad on that object.
//   2. (Optional) Set the `captureHotkey` field in the Inspector (default: None = disabled).
//      Press that key at runtime to trigger CaptureToGallery.
//   3. To wire a UI button: drag the ScreenshotService component into the Button's
//      OnClick() slot and select ScreenshotService → CaptureToGallery.
//   4. UIManager.Instance must be present in the scene; ScreenshotService loud-fails
//      if it is missing.

namespace Habitales.UI
{
    /// <summary>
    /// Captures the screen to a timestamped PNG in <see cref="GalleryPath"/>, hiding
    /// all registered UI subsystems for the frame so saved shots are UI-free.
    /// </summary>
    public class ScreenshotService : MonoBehaviour
    {
        // ─── Gallery path (S2 — one place, shared with GalleryController) ────

        /// <summary>
        /// Absolute path to the Screenshots folder.
        /// The directory is created on first capture if it does not exist.
        /// </summary>
        public static string GalleryPath =>
            Path.Combine(Application.persistentDataPath, "Screenshots");

        // ─── Serialized settings ──────────────────────────────────────────────

        [Header("Optional hotkey (KeyCode.None = disabled)")]
        [SerializeField] private KeyCode captureHotkey = KeyCode.None;

        // ─── State ────────────────────────────────────────────────────────────

        private bool _capturing;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        private void Update()
        {
            if (captureHotkey != KeyCode.None && Input.GetKeyDown(captureHotkey))
                CaptureToGallery();
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Hides all UI, waits one frame for the render to settle, captures the screen
        /// to a PNG in <see cref="GalleryPath"/>, then restores UI visibility.
        /// Safe to call from a UI Button's OnClick.
        /// </summary>
        public void CaptureToGallery() => CaptureCleanTexture(SaveToGallery);

        /// <summary>
        /// Hides all UI, waits one frame, captures the screen as a Texture2D, restores UI,
        /// then invokes <paramref name="onCaptured"/> with the result (null on failure).
        /// OWNERSHIP: the callback owns the texture and must Destroy() it when done.
        /// Used by RunManager for the UI-free peak-thriving snapshot.
        /// </summary>
        public void CaptureCleanTexture(Action<Texture2D> onCaptured)
        {
            if (_capturing)
            {
                Debug.LogWarning($"{name}: a capture is already running — ignoring duplicate call.", this);
                onCaptured?.Invoke(null);
                return;
            }

            if (UIManager.Instance == null)
            {
                Debug.LogError($"{name}: UIManager.Instance is null — cannot hide UI for screenshot. " +
                               "Ensure UIManager is present and initialised before ScreenshotService.", this);
                onCaptured?.Invoke(null);
                return;
            }

            StartCoroutine(CaptureRoutine(onCaptured));
        }

        // ─── Capture coroutine ────────────────────────────────────────────────

        private IEnumerator CaptureRoutine(Action<Texture2D> onCaptured)
        {
            _capturing = true;

            // 1. Hide all registered subsystems (§5.1 master-visibility sweep).
            UIManager.Instance.SetAllUIVisible(false);

            // 2. Wait for the end of the frame so the GPU has rendered the UI-free scene.
            yield return new WaitForEndOfFrame();

            // 3. Capture the rendered frame.
            Texture2D screenshot = null;
            try
            {
                // ScreenCapture.CaptureScreenshotAsTexture captures the full display.
                screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{name}: Screen capture failed — {ex.Message}", this);
            }

            // 4. Restore UI regardless of whether the capture succeeded.
            UIManager.Instance.RestoreUIVisibility();

            _capturing = false;

            // 5. Hand the texture (or null) to the caller — they own it now.
            onCaptured?.Invoke(screenshot);
        }

        // ─── Gallery sink ─────────────────────────────────────────────────────

        /// <summary>Writes the captured texture to a timestamped PNG, then destroys it.</summary>
        private void SaveToGallery(Texture2D screenshot)
        {
            if (screenshot == null) return;

            try
            {
                EnsureDirectoryExists();

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string filename  = $"Habitales_{timestamp}.png";
                string fullPath  = Path.Combine(GalleryPath, filename);

                byte[] pngBytes = screenshot.EncodeToPNG();
                File.WriteAllBytes(fullPath, pngBytes);

                Debug.Log($"{name}: Screenshot saved → {fullPath}", this);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{name}: Failed to write screenshot to disk — {ex.Message}", this);
            }
            finally
            {
                Destroy(screenshot);
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(GalleryPath))
                Directory.CreateDirectory(GalleryPath);
        }
    }
}
