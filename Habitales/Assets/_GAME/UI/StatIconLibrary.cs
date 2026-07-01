using System.Collections.Generic;
using UnityEngine;

namespace Habitales.UI
{
    /// <summary>
    /// Single authorable source mapping an <see cref="IndicatorStatId"/> to its icon sprite,
    /// display name, and description (the description is reused verbatim as the tooltip body).
    /// One place for icon + label + tooltip text (S2) so the world bar, region bar, and the
    /// (later) inspect-panel substats all read identical content.
    ///
    /// WIRING (human): Assets ▶ Create ▶ Habitales ▶ UI ▶ Stat Icon Library, then add one
    /// entry per id with sprite + name + description. A starter asset with placeholder
    /// sprites already exists at _SO/UI/StatIconLibrary.asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Habitales/UI/Stat Icon Library", fileName = "StatIconLibrary")]
    public class StatIconLibrary : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public IndicatorStatId id;
            public Sprite          icon;
            public string          displayName;
            [TextArea] public string description; // shown as the tooltip body
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<IndicatorStatId, Entry> _lookup;

        // Rebuild the lookup lazily; invalidated on edit so Inspector changes take effect.
        private void OnEnable()   => _lookup = null;
        private void OnValidate() => _lookup = null;

        private void BuildLookup()
        {
            _lookup = new Dictionary<IndicatorStatId, Entry>();
            foreach (var e in entries)
            {
                if (_lookup.ContainsKey(e.id))
                {
                    Debug.LogError($"{name}: duplicate StatIconLibrary entry for '{e.id}' — keys must be unique.", this);
                    continue;
                }
                _lookup[e.id] = e;
            }
        }

        /// <summary>
        /// Looks up an entry. Loud-fails (Law 3) and returns false on a miss so callers can
        /// fall back gracefully rather than NRE.
        /// </summary>
        public bool TryGet(IndicatorStatId id, out Entry entry)
        {
            if (_lookup == null) BuildLookup();
            if (_lookup.TryGetValue(id, out entry)) return true;

            Debug.LogError($"{name}: no entry for '{id}'. Add one in the Inspector.", this);
            entry = default;
            return false;
        }

        public Sprite GetIcon(IndicatorStatId id)        => TryGet(id, out var e) ? e.icon        : null;
        public string GetDisplayName(IndicatorStatId id) => TryGet(id, out var e) ? e.displayName : id.ToString();
        public string GetDescription(IndicatorStatId id) => TryGet(id, out var e) ? e.description : string.Empty;
    }
}
