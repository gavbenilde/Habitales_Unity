using System.Collections.Generic;
using UnityEngine;

namespace Habitales.Onboarding
{
    /// <summary>
    /// Owns the dusk-bubble object pool and the Law-2 meaning-event hook. Subscribes to
    /// <c>TileManager.OnEntitySpawned</c> (fires on plant-placed AND stage promotion/evolve)
    /// and pops a pooled <see cref="DuskBubble"/> at the tile's world position. Event-driven
    /// only — never per-tile-per-day. The pool size is the hard cap on concurrent bubbles, so
    /// a region-generation burst can never spawn thousands of objects.
    /// </summary>
    public class DuskBubblePool : MonoBehaviour
    {
        [Header("Pool")]
        [SerializeField] private DuskBubble bubblePrefab;
        [SerializeField] private int poolSize = 16;
        [SerializeField] private Transform poolParent;   // optional; defaults to this transform

        [Header("Filter")]
        [Tooltip("When on, only spawn a bubble if the spawned entityId contains one of these " +
                 "substrings (case-insensitive). Keeps bubbles to plants — no fire/trash/building " +
                 "bubbles, and no flood during a region-gen burst of non-plant entities.")]
        [SerializeField] private bool filterToPlants = true;
        [SerializeField] private string[] plantIdSubstrings = { "tree", "narra", "seedling", "sapling", "mature", "cover" };

        private readonly Queue<DuskBubble> _idle = new Queue<DuskBubble>();
        private bool _ok;

        void Awake()
        {
            if (bubblePrefab == null)
            {
                Debug.LogError($"{name}: DuskBubblePool has no bubblePrefab — no dusk bubbles will show. Assign a DuskBubble prefab in the Inspector.", this);
                enabled = false;
                return;
            }
            if (poolParent == null) poolParent = transform;

            for (int i = 0; i < Mathf.Max(1, poolSize); i++)
            {
                DuskBubble b = Instantiate(bubblePrefab, poolParent);
                b.gameObject.SetActive(false);
                _idle.Enqueue(b);
            }
            _ok = true;
        }

        void Start()
        {
            if (!_ok) return;
            if (TileManager.Instance != null)
                TileManager.Instance.OnEntitySpawned += HandleEntitySpawned;
            else
                Debug.LogWarning($"{name}: TileManager.Instance is null at Start — dusk bubbles won't fire on spawns.", this);
        }

        void OnDestroy()
        {
            if (TileManager.Instance != null)
                TileManager.Instance.OnEntitySpawned -= HandleEntitySpawned;
        }

        // ── Law-2 meaning-event hook ──────────────────────────────────────

        private void HandleEntitySpawned(Tile tile, string entityId)
        {
            if (!_ok || tile == null) return;
            if (filterToPlants && !MatchesPlant(entityId)) return;

            Vector3 worldPos = TileManager.Instance != null
                ? TileManager.Instance.GridToWorldPosition(tile.gridPosition)
                : Vector3.zero;
            Spawn(worldPos);
        }

        // ── Pool API ──────────────────────────────────────────────────────

        /// <summary>Pop an idle bubble and play it at worldPos. Returns null if the pool is exhausted (the cap).</summary>
        public DuskBubble Spawn(Vector3 worldPos)
        {
            if (_idle.Count == 0) return null;   // pool size is the concurrent-bubble cap
            DuskBubble b = _idle.Dequeue();
            b.gameObject.SetActive(true);
            b.PlayPooled(worldPos, this);
            return b;
        }

        /// <summary>Return a finished bubble to the pool (called by <see cref="DuskBubble"/> on complete).</summary>
        public void Return(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            DuskBubble b = go.GetComponent<DuskBubble>();
            if (b != null) _idle.Enqueue(b);
        }

        private bool MatchesPlant(string entityId)
        {
            if (string.IsNullOrEmpty(entityId) || plantIdSubstrings == null) return false;
            for (int i = 0; i < plantIdSubstrings.Length; i++)
            {
                string s = plantIdSubstrings[i];
                if (!string.IsNullOrEmpty(s) && entityId.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
