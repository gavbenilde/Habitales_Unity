using UnityEngine;
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
// Only the chosen kind's slot is shown in the Inspector; the other is hidden by
// ActionEffectDrawer (see Editor/ActionEffectDrawer.cs). We can't reuse TileEntitySO's
// [EnableIf] here because that conditional misbehaves on a field nested inside a struct
// that's a reorderable list element — which is what each effect is.
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

        [Tooltip("Entity definition to spawn on the tile (only placed if the tile is empty).")]
        public TileEntitySO entityToPlace;

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
                    if (entityToPlace == null) break;
                    if (tile.entity == null)
                    {
                        tileManager.SpawnById(tile, entityToPlace.EntityId);
                    }
                    else
                    {
                        // Reachable despite CanTargetTile: the tile was empty when the player
                        // selected it, and something (factory trash, fire spread) claimed it during
                        // the days the action was working — ActionManager applies each day's batch
                        // as that day comes. Loud rather than silent (Law 3): the player paid days
                        // and fatigue for a plant that isn't there.
                        Debug.LogWarning($"PlaceEntity '{entityToPlace.EntityId}' skipped at " +
                                         $"{tile.gridPosition}: tile is occupied by '{tile.entity.entityId}'.");
                    }
                    break;

                case ActionEffectType.CustomBehavior:
                    if (customBehaviour != null)
                        customBehaviour.Apply(tile, tileManager);
                    break;
            }
        }

        /// <summary>
        /// This effect's opinion on whether the action may be AIMED at <paramref name="tile"/>, in
        /// the same three-valued shape as <see cref="ActionEffectHook.CanTargetTile"/>: true/false
        /// = an opinion, null = no opinion (GenericPlayerAction ORs the opinions together).
        ///
        /// <para>PlaceEntity refuses occupied tiles because <see cref="Apply"/> refuses them — before
        /// this (2026-07-29) a plant action would happily take a trash/tree tile, burn its days and
        /// fatigue, and place nothing, which reads as "my buckwheat never grew".</para>
        /// </summary>
        public bool? CanTargetTile(Tile tile)
        {
            switch (type)
            {
                case ActionEffectType.PlaceEntity:
                    if (entityToPlace == null) return null;      // not authored yet — stay silent
                    return tile != null && tile.entity == null;

                case ActionEffectType.CustomBehavior:
                    return customBehaviour != null ? customBehaviour.CanTargetTile(tile) : null;

                default:
                    return null;
            }
        }
    }
}
