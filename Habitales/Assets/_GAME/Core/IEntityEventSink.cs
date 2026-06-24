using UnityEngine;

// Meaning-event seam (arch §6 — STUB). Entities receive an IEntityEventSink via their
// TickContext (Phase 4) to report meaningful world-moments (Law 2) WITHOUT knowing who
// listens. This is the decoupling seam the story-weaver / messaging app subscribe to.
//
// ⚠️ VOCABULARY IS DEFERRED (arch §6.2). Do NOT expand this into a rich event registry
// without a user decision — the "curated single-tier vs two-tier" question is its own
// design thread. Until then: `Raise(EntityEvent)` is the forward-compatible catch-all,
// plus the two most obvious named hooks from the §6.1 HOOK list. Wire the seam; stop.
//
// The `// HOOK:` insertion points in RunManager / TileManager / ResourceManager are added
// during each of those systems' ports (they don't exist as seams yet).
namespace Habitales.Core
{
    public interface IEntityEventSink
    {
        // Catch-all — the forward-compatible path until the vocabulary is designed.
        void Raise(EntityEvent e);

        // The two obvious entity meaning-moments (arch §6.1). Identified by entityId +
        // grid position rather than a Tile reference, to stay decoupled from Tile's type.
        void EntitySpawned(string entityId, Vector2Int gridPosition);
        void EntityDied(string entityId, Vector2Int gridPosition, string cause);
    }

    // Minimal catch-all payload. Fields kept deliberately generic (string `kind`) until
    // the event-vocabulary thread settles the registry shape.
    public struct EntityEvent
    {
        // Convention for the catch-all path: a hook that wants to trigger a scripted
        // GameEventSO sets kind = ScriptedEvent and puts the event ID in `cause`. The real
        // sink translates it to EventManager.FireEventByID. This keeps the interface frozen
        // (no per-event methods) while still letting hooks fire scripted events decoupled (S1).
        public const string ScriptedEvent = "scripted_event";

        public string      kind;          // e.g. "entity_died", "entity_spawned" (STUB — string for now)
        public string      entityId;
        public Vector2Int  gridPosition;
        public string      cause;         // optional context
    }

    // No-op sink so TickContext always carries a non-null sink (no null-checks at call sites).
    // The real sink (an EventManager adapter) is wired during the heartbeat port (Phase 3).
    public sealed class NullEntityEventSink : IEntityEventSink
    {
        public static readonly NullEntityEventSink Instance = new NullEntityEventSink();
        public void Raise(EntityEvent e) { }
        public void EntitySpawned(string entityId, Vector2Int gridPosition) { }
        public void EntityDied(string entityId, Vector2Int gridPosition, string cause) { }
    }

    // The REAL sink — bridges entity meaning-moments to the live game systems so entities never
    // grab a singleton mid-tick (S1). RunManager builds one and threads it into every TickContext.
    //
    // Today it only translates the catch-all scripted-event path (kind == ScriptedEvent → fire the
    // GameEventSO whose ID is in `cause`); EntitySpawned/EntityDied are no-ops because TileManager
    // already fires its own OnEntitySpawned/OnEntityDied at the mutation site (§6.1) and the rich
    // vocabulary is deferred (§6.2). It is the single place to grow that routing when the
    // event-vocabulary thread lands — keep the routing here, never back in the entities.
    public sealed class EventManagerEntitySink : IEntityEventSink
    {
        public void Raise(EntityEvent e)
        {
            switch (e.kind)
            {
                case EntityEvent.ScriptedEvent:
                    if (!string.IsNullOrEmpty(e.cause))
                        EventManager.Instance?.FireEventByID(e.cause);
                    break;
                // Other kinds: no live consumer yet — vocabulary deferred (§6.2).
            }
        }

        public void EntitySpawned(string entityId, Vector2Int gridPosition) { }
        public void EntityDied(string entityId, Vector2Int gridPosition, string cause) { }
    }
}
