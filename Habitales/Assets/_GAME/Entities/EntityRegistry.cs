using System.Collections.Generic;
using UnityEngine;

// EntityRegistry — the single `entityId → TileEntitySO` lookup (arch §5.4 / §3.6).
// THREE consumers share this one registry: the entity system (spawn/transform),
// Regions (forced-entity placement, replacing the string switch), and persistence
// (load = registry lookup + rehydrate). The prototype's GeneratedPlantRegistry retires;
// this is the sole registry going forward.
//
// Populated as a project asset (drag every TileEntitySO into `entities`), validated at
// boot by GameBootstrap.
namespace Habitales.Entities
{
    [CreateAssetMenu(menuName = "Habitales/Entities/Entity Registry")]
    public class EntityRegistry : ScriptableObject
    {
        [Tooltip("Every TileEntitySO in the project. The single source of entityId → definition.")]
        public List<TileEntitySO> entities = new List<TileEntitySO>();

        private Dictionary<string, TileEntitySO> _byId;

        // Keyed by each entity's derived EntityId (TileEntitySO.GenerateId(displayName)) —
        // ids are never hand-authored (design decision 2026-07-07).
        public void Build()
        {
            _byId = new Dictionary<string, TileEntitySO>();
            foreach (var e in entities)
            {
                if (e == null || string.IsNullOrEmpty(e.EntityId)) continue;
                _byId[e.EntityId] = e;
            }
        }

        // Lookup. Accepts either a raw id OR a display name — both resolve through the same
        // GenerateId slug, so callers can pass whichever they have. A miss is a LOUD error
        // (Law 3) — saved tiles referencing a missing id cannot rehydrate; never a silent skip.
        public TileEntitySO Get(string idOrDisplayName)
        {
            if (_byId == null) Build();
            string id = TileEntitySO.GenerateId(idOrDisplayName);
            if (_byId.TryGetValue(id, out var so)) return so;
            Debug.LogError($"EntityRegistry: no entity with id '{id}' (looked up from '{idOrDisplayName}'). " +
                           "Saved tiles referencing it cannot load — was the display name renamed?", this);
            return null;
        }

        // Loud-fail validation (Law 3) — called by GameBootstrap after boot.
        // Returns true if the registry is clean. Logs every problem with object context.
        public bool ValidateAll()
        {
            bool ok = true;
            var seen = new Dictionary<string, TileEntitySO>();
            foreach (var e in entities)
            {
                if (e == null)
                {
                    Debug.LogError("EntityRegistry: null entry in the entities list.", this);
                    ok = false;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(e.displayName))
                {
                    Debug.LogError($"EntityRegistry: '{e.name}' has a blank displayName.", e);
                    ok = false;
                    continue;
                }
                string id = e.EntityId;
                if (seen.TryGetValue(id, out var existing))
                {
                    Debug.LogError($"EntityRegistry: displayNames '{existing.displayName}' (on '{existing.name}') and " +
                                   $"'{e.displayName}' (on '{e.name}') both derive the id '{id}' — display names must " +
                                   "be unique enough that their slugs don't collide.", e);
                    ok = false;
                    continue;
                }
                seen[id] = e;
            }
            Build();
            return ok;
        }
    }
}
