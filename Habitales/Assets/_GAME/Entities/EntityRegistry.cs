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

        public void Build()
        {
            _byId = new Dictionary<string, TileEntitySO>();
            foreach (var e in entities)
            {
                if (e == null || string.IsNullOrEmpty(e.entityId)) continue;
                _byId[e.entityId] = e;
            }
        }

        // Lookup. A miss is a LOUD error (Law 3) — saved tiles referencing a missing id
        // cannot rehydrate; never a silent skip.
        public TileEntitySO Get(string entityId)
        {
            if (_byId == null) Build();
            if (_byId.TryGetValue(entityId, out var so)) return so;
            Debug.LogError($"EntityRegistry: no entity with id '{entityId}'. " +
                           "Saved tiles referencing it cannot load — was the id renamed?", this);
            return null;
        }

        // Loud-fail validation (Law 3) — called by GameBootstrap after boot.
        // Returns true if the registry is clean. Logs every problem with object context.
        public bool ValidateAll()
        {
            bool ok = true;
            var seen = new HashSet<string>();
            foreach (var e in entities)
            {
                if (e == null)
                {
                    Debug.LogError("EntityRegistry: null entry in the entities list.", this);
                    ok = false;
                    continue;
                }
                if (string.IsNullOrEmpty(e.entityId))
                {
                    Debug.LogError($"EntityRegistry: '{e.name}' has a blank entityId.", e);
                    ok = false;
                    continue;
                }
                if (!seen.Add(e.entityId))
                {
                    Debug.LogError($"EntityRegistry: duplicate entityId '{e.entityId}' " +
                                   $"(on '{e.name}'). entityIds must be project-unique.", e);
                    ok = false;
                }
            }
            Build();
            return ok;
        }
    }
}
