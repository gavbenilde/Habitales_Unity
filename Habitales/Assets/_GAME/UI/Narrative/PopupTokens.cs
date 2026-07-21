using System.Text;
using UnityEngine;

namespace Habitales.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    // PopupTokens.cs — token substitution for PopupSO line bodies (ONBOARDING_HANDOFF.md §5).
    //
    // Authored PopupLine.body text may embed "{token}" spans that resolve from LIVE
    // run state at PRESENT-TIME (not baked at author time), e.g. "Big Boss Bob expects
    // a report after {days} Days. I'll check in at Day {checkinDay}."
    //
    // Resolution lives ONLY here, called from PopupSO.ResolveLines — the single
    // resolution path (S2) — so no caller can bypass it and ship an unresolved token.
    //
    // This is a SEPARATE mechanism from the global EventContext (Events/EventContext.cs):
    // EventContext serves ConversationSO/DialogueManager (chat), PopupTokens serves
    // PopupSO (popups). The two content types intentionally never share a resolution
    // path (Popup/Dialogue separation).
    //
    // ── Vocabulary ── keep this list SMALL — add tokens on demand per handoff §5,
    // don't pre-build a general templating system:
    //   {days}       — ResourceManager.RunLengthDays  (total run length in days)
    //   {daysLeft}   — ResourceManager.DaysRemaining
    //   {seasons}    — run length in seasons (RunLengthDays / GameCalendar.DaysPerSeason)
    //   {checkinDay} — CheckInScheduler.CheckInIntervalDays (found lazily in-scene)
    //
    // ── Failure policy (Law 3 — loud-fail on misconfig) ──
    //   Unknown token             → Debug.LogError naming the token + the known
    //                               vocabulary; left LITERAL ("{token}") in the output
    //                               so an SO typo is obvious in play.
    //   Known token, live source
    //   unavailable right now     → Debug.LogWarning; left LITERAL in the output.
    //                               Never silently emits "0" or blank — a silent lie
    //                               is exactly what §5 warns against.
    //
    // Added 2026-07-21 (ONBOARDING_HANDOFF.md §5, item G).
    // ─────────────────────────────────────────────────────────────────────────

    public static class PopupTokens
    {
        // No CheckInScheduler singleton exists, so cache the scene lookup — Unity's
        // overloaded == treats a destroyed object as null, so this re-finds cleanly
        // across scene reloads without needing an explicit reset hook.
        private static CheckInScheduler _checkInScheduler;

        /// <summary>
        /// Resolves every "{token}" span in <paramref name="body"/> against live run
        /// state. Fast path: a string with no '{' (the vast majority of authored
        /// lines) is returned unchanged — no allocation, no logging.
        /// </summary>
        public static string Resolve(string body)
        {
            if (string.IsNullOrEmpty(body) || body.IndexOf('{') < 0) return body;

            var result = new StringBuilder();
            int i = 0;

            while (i < body.Length)
            {
                int open = body.IndexOf('{', i);
                if (open == -1)
                {
                    // No more tokens — append the rest and stop.
                    result.Append(body, i, body.Length - i);
                    break;
                }

                // Append everything before the opening brace.
                result.Append(body, i, open - i);

                int close = body.IndexOf('}', open + 1);
                if (close == -1)
                {
                    // Unterminated brace — copy the remainder literally, no crash.
                    result.Append(body, open, body.Length - open);
                    break;
                }

                string token = body.Substring(open + 1, close - open - 1);
                result.Append(ResolveToken(token));
                i = close + 1;
            }

            return result.ToString();
        }

        private static string ResolveToken(string token)
        {
            switch (token)
            {
                case "days":
                    return ResourceManager.Instance != null
                        ? ResourceManager.Instance.RunLengthDays.ToString()
                        : Unresolved(token);

                case "daysLeft":
                    return ResourceManager.Instance != null
                        ? ResourceManager.Instance.DaysRemaining.ToString()
                        : Unresolved(token);

                case "seasons":
                    if (ResourceManager.Instance == null) return Unresolved(token);
                    int seasons = GameCalendar.DaysPerSeason > 0
                        ? Mathf.Max(1, ResourceManager.Instance.RunLengthDays / GameCalendar.DaysPerSeason)
                        : 0;
                    return seasons.ToString();

                case "checkinDay":
                    if (_checkInScheduler == null)
                        _checkInScheduler = Object.FindObjectOfType<CheckInScheduler>();
                    return _checkInScheduler != null
                        ? _checkInScheduler.CheckInIntervalDays.ToString()
                        : Unresolved(token);

                default:
                    Debug.LogError($"PopupTokens: unknown token \"{{{token}}}\" — known tokens are days, daysLeft, seasons, checkinDay. Fix the PopupSO line body or add the token to PopupTokens.Resolve.");
                    return "{" + token + "}";
            }
        }

        /// <summary>
        /// A KNOWN token whose live source isn't reachable right now (e.g. no
        /// ResourceManager or CheckInScheduler in this scene). Logs a warning and
        /// leaves the token literal rather than silently emitting "0" — a silent
        /// wrong number is worse than a visible placeholder (§5).
        /// </summary>
        private static string Unresolved(string token)
        {
            Debug.LogWarning($"PopupTokens: \"{{{token}}}\" has no live source available right now — leaving it literal.");
            return "{" + token + "}";
        }
    }
}
