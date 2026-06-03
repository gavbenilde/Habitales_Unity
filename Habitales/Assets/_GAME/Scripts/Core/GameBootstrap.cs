using UnityEngine;

// GameBootstrap — single MonoBehaviour that validates/initializes every core
// singleton in a known sequence, ending initialization-order races (arch §4).
//
// PHASE 0c (now): skeleton + the documented init order. It does NOT yet validate
// live singletons, because the managers are renamed/renovated in later phases and
// EntityRegistry / the re-housed ProgressionPersistence do not exist yet.
//
// PHASE 3 (later): wire the real validation. Each step loud-fails (Law 3) with a
// `Debug.LogError(msg, this)` if its `Instance` is null, then sets enabled = false
// so no later frame proceeds on a broken boot.
namespace Habitales.Core
{
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        void Awake()
        {
            if (GameLog.Core)
                Debug.Log("[GameBootstrap] Boot starting — live validation is wired in Phase 3.", this);

            // ── Phase 3 init/validation order (each loud-fails on a null Instance) ──
            //   1. ResourceManager
            //   2. WeatherManager
            //   3. TileManager
            //   4. RegionManager   (renamed from ZoneManager in Phase 2.5)
            //   5. ActionManager
            //   6. EventManager
            //   7. DialogueManager
            //   8. RunManager
            //
            // After step 8:
            //   • EntityRegistry.ValidateAll()        — loud-fail on duplicate / blank entityId
            //   • ProgressionPersistence.Load(...)    — the SINGLE load point
            //       (removes today's dual-load in ActionManager.Awake + RunManager.Start)
        }
    }
}
