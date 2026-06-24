using System.Collections.Generic;
using UnityEngine;

// ActionRegistry — the project asset listing every authored ActionSO (arch §3.4 / §5.4),
// the action-side twin of EntityRegistry. ActionManager reads it at boot and wraps each
// entry in a GenericPlayerAction. Populated by hand: drag every ActionSO into `actions`.
namespace Habitales.Actions
{
    [CreateAssetMenu(menuName = "Habitales/Actions/Action Registry")]
    public class ActionRegistry : ScriptableObject
    {
        [Tooltip("Every authored ActionSO that should be playable. Order is the in-UI order.")]
        public List<ActionSO> actions = new List<ActionSO>();

        // Loud-fail validation (Law 3) — blank/duplicate actionIds are author errors.
        // Returns true if clean; logs every problem with object context.
        public bool ValidateAll()
        {
            bool ok = true;
            var seen = new HashSet<string>();
            foreach (var a in actions)
            {
                if (a == null)
                {
                    Debug.LogError("ActionRegistry: null entry in the actions list.", this);
                    ok = false;
                    continue;
                }
                if (string.IsNullOrEmpty(a.actionId))
                {
                    Debug.LogError($"ActionRegistry: '{a.name}' has a blank actionId.", a);
                    ok = false;
                    continue;
                }
                if (!seen.Add(a.actionId))
                {
                    Debug.LogError($"ActionRegistry: duplicate actionId '{a.actionId}' " +
                                   $"(on '{a.name}'). actionIds must be project-unique.", a);
                    ok = false;
                }
            }
            return ok;
        }
    }
}
