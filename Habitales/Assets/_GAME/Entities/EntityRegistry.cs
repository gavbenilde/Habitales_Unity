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

        // Reverse stage map (child -> parents), built once at boot. Chains are forward-linked ONLY
        // via TileEntitySO.nextStage — there is no back-pointer on the SO itself, so
        // AuthoredContent.Source.FirstStageInChain resolution needs this to walk backward to a
        // chain's root. A child reachable from 2+ parents (shared DeadTree/Stump assets) is
        // ambiguous — resolving FirstStageInChain THROUGH it is a registry-validation loud-fail.
        private Dictionary<TileEntitySO, List<TileEntitySO>> _reverseStage;

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

            _reverseStage = new Dictionary<TileEntitySO, List<TileEntitySO>>();
            foreach (var e in entities)
            {
                if (e == null || e.nextStage == null) continue;
                if (!_reverseStage.TryGetValue(e.nextStage, out var parents))
                {
                    parents = new List<TileEntitySO>();
                    _reverseStage[e.nextStage] = parents;
                }
                parents.Add(e);
            }
        }

        // ── Azi tier content resolution ─────────────────────────────────────────────────────────

        /// <summary>
        /// Walks the reverse stage map backward from <paramref name="so"/> to the FIRST stage of
        /// its chain (the stage with no parent). Returns null (and loud-fails, Law 3) if the walk
        /// hits a cycle or a stage reachable from 2+ parents (an ambiguous "first stage" — e.g. a
        /// shared DeadTree/Stump asset used by multiple species' chains).
        /// </summary>
        public TileEntitySO ResolveFirstStage(TileEntitySO so)
        {
            if (_byId == null) Build();
            if (so == null) return null;

            var visited = new HashSet<TileEntitySO>();
            TileEntitySO current = so;
            while (true)
            {
                if (!visited.Add(current))
                {
                    Debug.LogError($"EntityRegistry: cycle detected in the stage chain while resolving the first stage from " +
                                   $"'{so.name}' (revisited '{current.name}').", current);
                    return null;
                }
                if (!_reverseStage.TryGetValue(current, out var parents) || parents.Count == 0)
                    return current; // root of the chain
                if (parents.Count > 1)
                {
                    Debug.LogError($"EntityRegistry: '{current.name}' is reachable from {parents.Count} parents " +
                                   $"({string.Join(", ", parents.ConvertAll(p => p.name))}) — its first stage is ambiguous. " +
                                   "A TileEntitySO using Source.FirstStageInChain cannot resolve through a shared/convergent " +
                                   "stage like this (e.g. a shared DeadTree/Stump). Author it as Default or FromEntitySO instead.", current);
                    return null;
                }
                current = parents[0];
            }
        }

        /// <summary>
        /// Resolves one AuthoredContent field (tier1Intro or tier4JournalEntry — pass a selector so
        /// resolution always compares like-for-like) per the three Source rules. Returns null (with
        /// a loud Debug.LogError, Law 3) when unresolvable — callers decide their own runtime
        /// fallback text; this method never invents content.
        /// </summary>
        public string ResolveContent(TileEntitySO so, System.Func<TileEntitySO, AuthoredContent> fieldSelector)
        {
            if (so == null || fieldSelector == null) return null;
            AuthoredContent content = fieldSelector(so);
            if (content == null) return null;

            switch (content.source)
            {
                case AuthoredContent.Source.Default:
                    return content.text;

                case AuthoredContent.Source.FirstStageInChain:
                {
                    TileEntitySO first = ResolveFirstStage(so); // loud-fails internally on ambiguity/cycle
                    if (first == null) return null;
                    AuthoredContent firstContent = fieldSelector(first);
                    if (firstContent == null || firstContent.source != AuthoredContent.Source.Default || string.IsNullOrWhiteSpace(firstContent.text))
                    {
                        Debug.LogError($"EntityRegistry: '{so.name}' resolves FirstStageInChain to '{first.name}', but that stage's " +
                                       "own content is not authored as non-empty Default text — cannot resolve. Author the first stage's field directly.", first);
                        return null;
                    }
                    return firstContent.text;
                }

                case AuthoredContent.Source.FromEntitySO:
                {
                    if (content.sourceSO == null)
                    {
                        Debug.LogError($"EntityRegistry: '{so.name}' has an AuthoredContent field set to FromEntitySO but sourceSO is unassigned.", so);
                        return null;
                    }
                    AuthoredContent borrowed = fieldSelector(content.sourceSO);
                    if (borrowed == null || borrowed.source != AuthoredContent.Source.Default || string.IsNullOrWhiteSpace(borrowed.text))
                    {
                        Debug.LogError($"EntityRegistry: '{so.name}' borrows content from '{content.sourceSO.name}' via FromEntitySO, " +
                                       "but that SO's own field is not Default non-empty text — only a ONE-HOP borrow off directly-authored text is allowed.", so);
                        return null;
                    }
                    return borrowed.text;
                }

                default:
                    return null;
            }
        }

        /// <summary>Loud-fail check for a FirstStageInChain field (used by ValidateAll) — logs via
        /// ResolveContent/ResolveFirstStage and returns whether it resolved.</summary>
        private bool ValidateAuthoredContentChain(TileEntitySO so, System.Func<TileEntitySO, AuthoredContent> selector, string fieldName)
        {
            AuthoredContent content = selector(so);
            if (content == null || content.source != AuthoredContent.Source.FirstStageInChain) return true;
            string resolved = ResolveContent(so, selector);
            if (resolved == null)
            {
                Debug.LogError($"EntityRegistry: '{so.name}'.{fieldName} uses Source.FirstStageInChain but could not resolve (see error above).", so);
                return false;
            }
            return true;
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
                    ok = false;
                    continue;
                }
                seen[id] = e;
            }
            Build(); // also builds the reverse stage map, needed by the chain checks below

            // Azi tier content — FirstStageInChain resolvability. The "empty" case is caught
            // per-asset by TileEntitySO.OnValidate; this is the ONLY place an ambiguous/cyclic
            // chain can be caught, since it needs the whole graph.
            foreach (var e in entities)
            {
                if (e == null) continue;
                if (!ValidateAuthoredContentChain(e, so => so.tier1Intro, nameof(e.tier1Intro))) ok = false;
                if (!ValidateAuthoredContentChain(e, so => so.tier4JournalEntry, nameof(e.tier4JournalEntry))) ok = false;
            }

            return ok;
        }
    }
}
