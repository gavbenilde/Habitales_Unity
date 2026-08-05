using System.Collections.Generic;
using UnityEngine;

// Roam-only Walker. Carries no state Walker doesn't already have; the only addition is the
// follow-chance roll. Species differences (carabao vs warty pig) are WalkerProfileSO data
// (moveSpeed, followChance, ...), never subclasses — one class covers every animal species.
// Future behaviours (e.g. fleeing nearby Working workers) would live here too; not built yet.
[DisallowMultipleComponent]
public class AnimalWalker : Walker
{
    // Reused across follow-pick calls — mirrors Walker's own no-per-journey-allocation approach
    // for the one list this override needs (the base's WeightedPick still takes a List<Tile>).
    private readonly List<Tile> followCandidates = new List<Tile>();

    protected override float EffectiveMoveSpeed()
    {
        float speed = profile.moveSpeed;
        speed *= TimeFlowSignal.SpeedFactor;
        
        return speed;
    }
    
    /// <summary>Rolls followChance; on success, targets a tile with exactly one WorkerWalker
    /// claim instead of an empty one, so the animal walks over and stands with the worker.
    /// Falls back to the normal empty-tile pick otherwise.</summary>
    protected override Tile PickRoamDestination()
    {
        if (profile.followChance > 0f && Random.value < profile.followChance)
        {
            Tile followTarget = PickFollowDestination();
            if (followTarget != null) return followTarget;
        }
        return base.PickRoamDestination();
    }

    private Tile PickFollowDestination()
    {
        followCandidates.Clear();
        foreach (Tile t in manager.TilesWithExactlyOneWorkerClaim())
        {
            if (t == currentTile) continue;
            if (GridDistance(currentTile.gridPosition, t.gridPosition) > profile.destinationRadius) continue;
            followCandidates.Add(t);
        }
        return followCandidates.Count > 0 ? WeightedPick(followCandidates) : null;
    }
}
