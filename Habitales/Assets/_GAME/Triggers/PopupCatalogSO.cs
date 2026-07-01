using System.Collections.Generic;
using UnityEngine;
using Habitales.UI;

namespace Habitales.Triggers
{
    // ─────────────────────────────────────────────────────────────────────────
    // PopupCatalogSO.cs — registry of all PopupSO assets keyed by eventName.
    //
    // Mirrors the lazy-cache pattern used by GameEventRegistry + DialogueRegistry.
    // TriggerManager holds a reference to this asset and calls GetById(id) to
    // look up a popup before firing it.
    //
    // Create via: Assets ▶ Create ▶ Habitales ▶ Popup Catalog
    //
    // Added 2026-06-30 (WO-3, TriggerManager).
    // ─────────────────────────────────────────────────────────────────────────

    [CreateAssetMenu(fileName = "PopupCatalog", menuName = "Habitales/Popup Catalog")]
    public class PopupCatalogSO : ScriptableObject
    {
        [Tooltip("All PopupSO assets in this catalog. Each entry's eventName is the lookup key for Fire(id).")]
        public List<PopupSO> popups = new List<PopupSO>();

        // Lazy cache built on first GetById call; invalidated by OnValidate.
        private Dictionary<string, PopupSO> _byId;

        /// <summary>
        /// Returns the <see cref="PopupSO"/> whose <c>eventName</c> matches <paramref name="id"/>.
        /// Logs a warning and returns <c>null</c> if the id is missing or duplicated.
        /// </summary>
        public PopupSO GetById(string id)
        {
            if (_byId == null)
                BuildCache();

            if (_byId.TryGetValue(id, out PopupSO result))
                return result;

            Debug.LogWarning($"PopupCatalogSO '{name}': no PopupSO found with eventName '{id}'.");
            return null;
        }

        // ─── Cache construction ───────────────────────────────────────────────

        private void BuildCache()
        {
            _byId = new Dictionary<string, PopupSO>();

            if (popups == null) return;

            foreach (PopupSO popup in popups)
            {
                if (popup == null)
                {
                    Debug.LogWarning($"PopupCatalogSO '{name}': null entry in popups list — remove the empty slot.");
                    continue;
                }

                if (string.IsNullOrEmpty(popup.eventName))
                {
                    Debug.LogWarning($"PopupCatalogSO '{name}': PopupSO asset '{popup.name}' has an empty eventName — it cannot be looked up by ID.");
                    continue;
                }

                if (_byId.ContainsKey(popup.eventName))
                {
                    Debug.LogWarning($"PopupCatalogSO '{name}': duplicate eventName '{popup.eventName}' on asset '{popup.name}'. " +
                                     "First entry wins; remove the duplicate.");
                    continue;
                }

                _byId[popup.eventName] = popup;
            }
        }

        // Invalidate the cache whenever the SO is modified in the Editor or hot-reloaded.
        private void OnValidate()
        {
            _byId = null;
        }
    }
}
