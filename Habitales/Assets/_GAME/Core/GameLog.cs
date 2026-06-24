// GameLog — central, category-tagged debug-logging toggles (arch §8).
// Replaces the fragmented per-manager `showDebugInfo` flags so you can mute one
// category (e.g. Cascade spam) while debugging another. As each system is ported,
// swap its local debug flag for the matching toggle here.
//
// Usage at call sites:
//   if (GameLog.Cascade) Debug.Log("...cascade detail...");
//
// Phase 0c: static toggles only. A GameLogConfigSO binding (Inspector control) can
// be added later without changing call sites.
namespace Habitales.Core
{
    public static class GameLog
    {
        public static bool Core      = true;   // bootstrap / lifecycle
        public static bool Action    = true;   // action execution
        public static bool Cascade   = false;  // per-tile diffusion — noisy, muted by default
        public static bool Dialogue  = true;   // chat / Azi
        public static bool RegionGen = true;   // region generation
        public static bool Entity    = false;  // per-entity daily updates — noisy, muted by default
    }
}
