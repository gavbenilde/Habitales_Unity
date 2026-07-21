using UnityEngine;
using Habitales.Entities;

// CriticalGlowController — Azi tier system.
//
// Drives the oscillating-red "this plant is dying" glow on entity sprites. Subscribes to
// RunManager.OnTileTierChanged (Law 2 meaning-event): glow ON on any downward crossing INTO
// Critical when the tile's entity is category Plant, OFF on any upward crossing OUT of
// Critical. Also reacts to TileManager.OnEntityDied / OnEntitySpawned / OnEntityEvolved
// (read-only subscriptions — TileManager.cs itself is not touched) so the glow tracks the
// entity that's actually standing on the tile, not just the tile's tier.
//
// ARCHITECTURE CHOICE — one CENTRAL controller, not one component per entity visual.
// EntityVisualizer GameObjects are ephemeral: TileManager destroys and re-instantiates them on
// every death (RemoveEntity), promotion (ReplaceWithSO), and fresh spawn (SpawnFromDef) — see
// TileManager.cs:634-681. A sibling component riding that same GameObject would be destroyed
// and recreated on every one of those transforms and would need a way to rediscover "was this
// tile glowing before my GameObject got replaced?" — which means the real state has to live
// somewhere that OUTLIVES the visual anyway. Centralizing that state here (keyed by the stable
// `Tile` reference) means: (a) one place owns "which tiles are currently glowing" (S2), (b) a
// promotion/respawn just re-derives the glow from tile.lastTier + the new entity's category —
// no rebind plumbing needed on the visual side, (c) zero footprint on the entityVisualizerPrefab
// beyond "this prefab has a SpriteRenderer" (already true), so no prefab edit is required to
// ship this — a human only has to swap in the new glow-capable material (see the shader spec).
//
// No material instancing: every SpriteRenderer keeps sharing the one `defaultMaterial` that
// EntityVisualizer already assigns (EntityVisualizer.cs Awake) — this controller only ever
// writes a MaterialPropertyBlock per-renderer, so GPU instancing/batching survives.
//
// Placement: put ONE of these on any persistent scene object (alongside RunManager/TileManager
// is fine). It is a singleton, like the other core managers (S4).
public class CriticalGlowController : MonoBehaviour
{
    public static CriticalGlowController Instance { get; private set; }

    [Header("Shader Property (must match the CriticalGlow shader spec)")]
    [Tooltip("Per-instance MaterialPropertyBlock float — 0 = no glow, 1 = glow oscillating per " +
             "the material's own speed/color/intensity. See CriticalGlowShaderSpec.md.")]
    [SerializeField] private string glowOnPropertyName = "_GlowOn";

    private readonly System.Collections.Generic.HashSet<Tile> glowingTiles =
        new System.Collections.Generic.HashSet<Tile>();

    private int glowOnPropertyId;
    private MaterialPropertyBlock scratchBlock;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        glowOnPropertyId = Shader.PropertyToID(glowOnPropertyName);
        scratchBlock = new MaterialPropertyBlock();
    }

    void Start()
    {
        if (RunManager.Instance == null)
        {
            Debug.LogError("[CriticalGlowController] RunManager.Instance is null at Start — cannot subscribe to OnTileTierChanged. Glow will never turn on. Check scene bootstrap order.", this);
            enabled = false;
            return;
        }
        RunManager.Instance.OnTileTierChanged += HandleTileTierChanged;

        if (TileManager.Instance == null)
        {
            Debug.LogError("[CriticalGlowController] TileManager.Instance is null at Start — cannot subscribe to entity lifecycle events. Glow will not survive death/promotion/respawn correctly.", this);
            enabled = false;
            return;
        }
        TileManager.Instance.OnEntityDied    += HandleEntityDied;
        TileManager.Instance.OnEntitySpawned += HandleEntitySpawned;
        TileManager.Instance.OnEntityEvolved += HandleEntityEvolved;
    }

    void OnDestroy()
    {
        if (RunManager.Instance != null)
            RunManager.Instance.OnTileTierChanged -= HandleTileTierChanged;

        if (TileManager.Instance != null)
        {
            TileManager.Instance.OnEntityDied    -= HandleEntityDied;
            TileManager.Instance.OnEntitySpawned -= HandleEntitySpawned;
            TileManager.Instance.OnEntityEvolved -= HandleEntityEvolved;
        }

        if (Instance == this) Instance = null;
    }

    // ── RunManager.OnTileTierChanged (Law 2 — fires only on crossing) ──────────────────────
    // Critical is the lowest tier, so ANY transition whose newTier == Critical is by
    // construction a downward crossing INTO Critical. ANY transition whose oldTier == Critical
    // is an upward crossing OUT of Critical.
    void HandleTileTierChanged(Tile tile, Tier oldTier, Tier newTier)
    {
        if (newTier == Tier.Critical)
        {
            RefreshGlowForTile(tile); // ON only if the current occupant is a Plant
        }
        else if (oldTier == Tier.Critical)
        {
            SetGlow(tile, false);
        }
    }

    // ── TileManager entity lifecycle (read-only subscriptions) ─────────────────────────────
    // A plant DYING always kills the glow outright — no re-check needed, the entity is gone.
    void HandleEntityDied(Tile tile, string entityId, string cause) => SetGlow(tile, false);

    // A fresh entity landing on an already-Critical tile (e.g. player clears debris and plants
    // into a Critical tile) doesn't produce a NEW OnTileTierChanged crossing — the tier didn't
    // change. Re-derive from tile.lastTier so this doesn't silently miss the warning.
    void HandleEntitySpawned(Tile tile, string entityId) => RefreshGlowForTile(tile);

    // A promotion/transform (ReplaceWithSO) destroys and rebuilds the EntityVisualizer, which
    // drops any MaterialPropertyBlock the old instance carried. Also: a Plant can promote INTO
    // a non-Plant stage (e.g. Sapling -> DeadTree, category Debris) — that must turn the glow
    // off even though the tile is still Critical, since T2/glow is Plant-only by design.
    void HandleEntityEvolved(Tile tile, string entityId) => RefreshGlowForTile(tile);

    /// <summary>Re-derives desired glow state from the tile's current tracked tier + its current
    /// occupant's category, and applies it. Single source of truth for "should this tile glow
    /// right now" so every entry point (crossing, spawn, evolve) agrees (S2).</summary>
    void RefreshGlowForTile(Tile tile)
    {
        bool shouldGlow = tile != null && tile.lastTier == Tier.Critical && IsPlant(tile);
        SetGlow(tile, shouldGlow);
    }

    static bool IsPlant(Tile tile) =>
        tile.entity is GenericTileEntity g && g.def != null && g.def.category == EntityCategory.Plant;

    /// <summary>Applies (or clears) the glow on the SpriteRenderer currently standing on `tile`,
    /// via MaterialPropertyBlock only (no material instancing — batching survives). No-ops
    /// quietly when the tile has no live visual (already removed / not yet spawned) — that is
    /// an expected, non-erroneous state here, not a wiring failure (Law 3 distinguishes the two).</summary>
    void SetGlow(Tile tile, bool on)
    {
        if (tile == null) return;

        if (on) glowingTiles.Add(tile);
        else    glowingTiles.Remove(tile);

        if (TileManager.Instance == null) return;
        EntityVisualizer visualizer = TileManager.Instance.GetEntityVisualizer(tile);
        if (visualizer == null) return;

        SpriteRenderer renderer = visualizer.GetComponent<SpriteRenderer>();
        if (renderer == null) return;

        renderer.GetPropertyBlock(scratchBlock);
        scratchBlock.SetFloat(glowOnPropertyId, on ? 1f : 0f);
        renderer.SetPropertyBlock(scratchBlock);
    }
}
