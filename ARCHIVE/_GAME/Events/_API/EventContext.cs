using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Global flat dictionary for runtime event token replacement.
/// Two layers: persistent global values (kept fresh by game systems)
/// and ephemeral call-site overrides (cleared after each event fires).
/// </summary>
public static class EventContext
{
    // Always-fresh values — written to by ResourceManager, ZoneManager, etc.
    private static readonly Dictionary<string, string> _global 
        = new Dictionary<string, string>();

    // Per-fire overrides — written just before FireEventByID, cleared after
    private static readonly Dictionary<string, string> _overrides
        = new Dictionary<string, string>();

// Optional world-space camera target — set before FireEventByID, cleared with overrides
    private static Vector3? focusTarget = null;

    public static void SetFocusTarget(Vector3 pos) => focusTarget = pos;
    public static Vector3? GetFocusTarget()        => focusTarget;
    public static void ClearFocusTarget()          => focusTarget = null;

    // ───────────────────────────────────────────
    // WRITE
    // ───────────────────────────────────────────

    /// <summary>
    /// Set a persistent global value. Call this from game systems
    /// (ResourceManager, ZoneManager, etc.) whenever a value changes.
    /// </summary>
    public static void Set(string key, string value)
        => _global[key] = value;

    /// <summary>
    /// Set a call-site override. Takes priority over global values.
    /// Cleared automatically after each event fires.
    /// </summary>
    public static void SetOverride(string key, string value)
        => _overrides[key] = value;

    // ───────────────────────────────────────────
    // READ + RESOLVE
    // ───────────────────────────────────────────

    /// <summary>
    /// Gets a value by key. Checks overrides first, then global.
    /// Returns the raw "{key}" token if nothing is found — visible in-game, easy to catch.
    /// </summary>
    public static string Get(string key)
    {
        if (_overrides.TryGetValue(key, out string ov)) return ov;
        if (_global.TryGetValue(key, out string gv)) return gv;
        return $"{{{key}}}"; // intentionally visible — surfaces missing keys fast
    }

    /// <summary>
    /// Scans text for {token} patterns and replaces each with its resolved value.
    /// Safe to call on any string — passes through text with no tokens unchanged.
    /// </summary>
    public static string Resolve(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        StringBuilder result = new StringBuilder();
        int i = 0;

        while (i < text.Length)
        {
            int open = text.IndexOf('{', i);
            if (open == -1)
            {
                // No more tokens — append the rest and stop
                result.Append(text, i, text.Length - i);
                break;
            }

            // Append everything before the opening brace
            result.Append(text, i, open - i);

            int close = text.IndexOf('}', open + 1);
            if (close == -1)
            {
                // Unclosed brace — treat as literal text
                result.Append(text, open, text.Length - open);
                break;
            }

            string key = text.Substring(open + 1, close - open - 1);
            result.Append(Get(key));
            i = close + 1;
        }

        return result.ToString();
    }

    // ───────────────────────────────────────────
    // LIFECYCLE
    // ───────────────────────────────────────────

    /// <summary>
    /// Clears call-site overrides. Called by EventManager after every fire.
    /// </summary>
    public static void ClearOverrides()
    {
        _overrides.Clear();
        focusTarget = null;
    }
    /// <summary>
    /// Full reset for a new run. Clears both layers.
    /// Global values will repopulate naturally on the first AdvanceTime/ZoneGenerated.
    /// </summary>
    
    public static void ResetForNewRun()
    {
        _global.Clear();
        _overrides.Clear();
        focusTarget = null;
    }
}