using Habitales.Entities;

// TileEntity — runtime entity base (arch §5.1). Post-Phase-4 the ONLY concrete subclass is
// GenericTileEntity; identity + data come from the TileEntitySO `def`. The legacy string
// `entityType`, the one-class-per-entity model, and the (Tile, TileManager) tick are all gone.
public abstract class TileEntity
{
    public float        health = 100f;

    // Identity + data (arch §5.1). Set on construction from the TileEntitySO.
    public string       entityId;   // stable id + save key (mirrors def.entityId)
    public TileEntitySO def;        // shared, immutable definition/data

    // Per-day tick (arch §5.2). RunManager assembles one TickContext per day and threads it via
    // TileManager.UpdateAllEntities. GenericTileEntity implements this.
    public abstract void OnDailyUpdate(Tile tile, in TickContext ctx);
}
