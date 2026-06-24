using System.Collections.Generic;
using UnityEngine;

public enum SelectionMode
{
    FloodFill,
    Adjacent,
    NonAdjacent,
    Single       // exactly one tile — routed through multi-select with the cap forced to 1
}

public abstract class PlayerAction
{
    public abstract string ActionName        { get; }
    public abstract string Description       { get; }
    public abstract SelectionMode selectionMode { get; }
    public abstract ActionCategory Category  { get; }

    public abstract int MinPeoplePerTile     { get; }
    public abstract int BaseDays             { get; }
    public abstract int MinDays              { get; }

    public virtual float FatigueMultiplierPerTile => 2.0f;

    public virtual string VariantGroupName => null;

    // Author-supplied card sprite. Null for legacy code-defined actions (the UI falls back
    // to ActionIconConfig); GenericPlayerAction returns its ActionSO.icon so data-driven
    // actions display their own art without a config entry.
    public virtual Sprite Icon => null;

    // ── Boogle ("?") panel data ──────────────────────────────────────────────
    // The author-facing encyclopedia text surfaced by the Boogle panel. Null/empty for
    // legacy code-defined actions; GenericPlayerAction returns its ActionSO's Boogle fields.
    public virtual string Lore => null;
    public virtual string InfoTooltip => null;
    public virtual IReadOnlyList<Sprite> SupplementaryImages => null;

    // ── Calculation helpers (unchanged) ─────────────────────────────────────
    public int GetMaxTiles(int availablePeople)
        => Mathf.Max(1, availablePeople / MinPeoplePerTile);

    public int CalculateDays(int availablePeople, int targetTiles)
    {
        if (targetTiles <= 0) return MinDays;
        float peoplePerTile   = (float)availablePeople / targetTiles;
        float efficiency      = peoplePerTile / MinPeoplePerTile;
        float efficiencyFactor = Mathf.Sqrt(efficiency);
        int calculatedDays    = Mathf.CeilToInt(BaseDays / efficiencyFactor);
        return Mathf.Max(MinDays, calculatedDays);
    }

    // ── Validation ──────────────────────────────────────────────────────────
    // Called by ActionManager before starting the action coroutine.
    // Override only for special preflight logic (e.g. FireSuppression).
    // Default: delegate to CanExecute.
    public virtual bool Execute(List<Tile> tiles, TileManager tileManager)
        => CanExecute(tiles);

    public virtual bool CanExecute(List<Tile> tiles)
        => tiles != null && tiles.Count > 0;

    // ── Per-tile work ────────────────────────────────────────────────────────
    // Called once per tile by ActionManager as each day completes.
    // Put all stat changes, entity spawns, and overlay ops here.
    // ActionManager calls UpdateTileVisual after this, so you do not need to.
    public abstract void ExecuteOnTile(Tile tile, TileManager tileManager);

    // ── Utility ─────────────────────────────────────────────────────────────
    protected static bool AnyTileHasEntity<T>(List<Tile> tiles) where T : TileEntity
    {
        foreach (Tile tile in tiles)
            if (tile.entity is T)
                return true;
        return false;
    }

    // entityId-based replacement for the type-check helper (arch §5.1). Post-Phase-4 entities are
    // all GenericTileEntity, so `is FireEntity` no longer works — match on the data-driven id.
    protected static bool AnyTileHasEntityId(List<Tile> tiles, string entityId)
    {
        if (tiles == null) return false;
        foreach (Tile tile in tiles)
            if (tile.entity != null && tile.entity.entityId == entityId)
                return true;
        return false;
    }
}