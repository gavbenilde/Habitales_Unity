using UnityEngine;

public abstract class PlayerAction {
    public abstract string ActionName { get; }
    public abstract string Description { get; }
    
    // Returns true if action was successful
    public abstract bool Execute(Tile targetTile, TileManager tileManager);
}
