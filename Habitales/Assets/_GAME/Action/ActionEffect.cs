using UnityEngine;
using ArtificeToolkit.Attributes;
using Habitales.Entities;

// ActionEffect — one authorable unit of "what an action does to a tile" (arch §3.4).
// An ActionSO holds a list of these; GenericPlayerAction runs them in order, once per
// selected tile. Replaces the hardcoded bodies of each PlayerAction.ExecuteOnTile.
//
// Two kinds, chosen by `type`:
//   PlaceEntity    — pure data: spawn a TileEntitySO by id (the data-driven version of
//                    `tileManager.SpawnById(...)` that PlantTreesAction wrote by hand).
//   CustomBehavior — escape hatch: delegate to an ActionEffectHook subclass asset.
//
// EnableIf hides the irrelevant field so only the chosen kind's slot shows (same pattern
// TileEntitySO uses for its conditional fields).
namespace Habitales.Actions
{
    public enum ActionEffectType
    {
        PlaceEntity,
        CustomBehavior
    }

    // NOTE: a struct (not a class) on purpose — every Artifice reorderable list in this
    // project (StatChange, StatCondition, …) uses a [Serializable] struct. Artifice's list
    // view mishandles class elements during drag-reorder (ArgumentOutOfRange in SwapChildren),
    // so mirror the proven value-type shape.
    [System.Serializable]
    public struct ActionEffect
    {
        [Tooltip("What kind of effect this is. The relevant field below appears for each.")]
        public ActionEffectType type;

        [EnableIf(nameof(type), ActionEffectType.PlaceEntity)]
        [Tooltip("Entity definition to spawn on the tile (only placed if the tile is empty).")]
        public TileEntitySO entityToPlace;

        [EnableIf(nameof(type), ActionEffectType.CustomBehavior)]
        [Tooltip("Bespoke effect asset — the escape hatch, twin of EntityBehaviourHook.")]
        public ActionEffectHook customBehaviour;

        // Applied once per selected tile by GenericPlayerAction.ExecuteOnTile.
        public void Apply(Tile tile, TileManager tileManager)
        {
            if (tile == null || tileManager == null) return;

            switch (type)
            {
                case ActionEffectType.PlaceEntity:
                    // Mirror the dominant existing pattern (PlantTreesAction): never overwrite
                    // an occupied tile. Authors wanting replacement use a CustomBehavior hook.
                    if (entityToPlace != null && tile.entity == null)
                        tileManager.SpawnById(tile, entityToPlace.entityId);
                    break;

                case ActionEffectType.CustomBehavior:
                    if (customBehaviour != null)
                        customBehaviour.Apply(tile, tileManager);
                    break;
            }
        }
    }
}
