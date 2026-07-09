using System;
using System.Collections.Generic;
using UnityEngine;

namespace Habitales.Dialogue
{
    /// <summary>
    /// How the significant tile deltas compare over the check-in window. Drives which
    /// authored variant an Azi check-in plays (see <see cref="CheckInConversationSO"/>).
    /// </summary>
    public enum CheckInTileTrend
    {
        /// <summary>Matches regardless of the improved/decayed comparison.</summary>
        Any,
        /// <summary>More tiles improved significantly than decayed.</summary>
        MoreImproved,
        /// <summary>More tiles decayed significantly than improved.</summary>
        MoreDecayed,
        /// <summary>Improved and decayed counts are equal (including 0/0).</summary>
        Balanced
    }

    /// <summary>
    /// One authored dialogue variant for a check-in: a condition set plus the
    /// <see cref="ConversationSO"/> to play when it matches. Referencing a ConversationSO
    /// (rather than inlining a node list) keeps every variant authorable with the existing
    /// ConversationEditor; its trigger/channel routing fields are simply ignored here —
    /// the check-in panel plays the thread directly, nothing is delivered to a chat tab.
    /// </summary>
    [Serializable]
    public class CheckInVariant
    {
        [Tooltip("Author-facing label only — never shown in game.")]
        public string note;

        [Header("Conditions (ALL must match)")]
        public CheckInTileTrend tileTrend = CheckInTileTrend.Any;

        [Tooltip("When on, this variant only matches while days-left is within [min, max] inclusive — e.g. a 'we're running out of time' tone for the late game.")]
        public bool limitByDaysLeft;
        public int minDaysLeft;
        public int maxDaysLeft = 999;

        [Header("Dialogue")]
        public ConversationSO conversation;

        public bool Matches(int improved, int decayed, int daysLeft)
        {
            switch (tileTrend)
            {
                case CheckInTileTrend.MoreImproved: if (improved <= decayed) return false; break;
                case CheckInTileTrend.MoreDecayed:  if (decayed <= improved) return false; break;
                case CheckInTileTrend.Balanced:     if (improved != decayed) return false; break;
            }

            if (limitByDaysLeft && (daysLeft < minDaysLeft || daysLeft > maxDaysLeft))
                return false;

            return true;
        }
    }

    /// <summary>
    /// The specialized check-in dialogue asset: an ordered variant list, walked top-to-bottom
    /// at check-in time; the FIRST variant whose conditions all match wins (author priority =
    /// list order, so put the most specific variants on top and end with an Any catch-all).
    ///
    /// Variant bodies may use the tokens {days}, {improved}, {decayed} — CheckInPanelUI sets
    /// them as EventContext overrides before resolving, so DialogueManager's normal
    /// EventContext.Resolve pass substitutes them (plus any global tokens) for free.
    ///
    /// AUTHORING (human):
    ///   1. Create one ConversationSO per tone (e.g. Conversation_CheckIn_Improving,
    ///      Conversation_CheckIn_Decaying, Conversation_CheckIn_Balanced) with the normal
    ///      conversation editor. Channel/trigger fields don't matter for check-ins.
    ///   2. Create this asset (Habitales/Dialogue/Check-In Conversation), add one variant per
    ///      tone, set conditions, and drag each ConversationSO in.
    ///   3. Keep the LAST variant as tileTrend = Any with no days-left limit — the guaranteed
    ///      catch-all. Select() loud-warns and falls back to the first wired conversation if
    ///      nothing matches.
    /// </summary>
    [CreateAssetMenu(fileName = "CheckIn_New", menuName = "Habitales/Dialogue/Check-In Conversation")]
    public class CheckInConversationSO : ScriptableObject
    {
        public List<CheckInVariant> variants = new List<CheckInVariant>();

        /// <summary>
        /// Picks the conversation to play for this check-in: first variant (in list order)
        /// whose conditions all match. Never silently returns null while any variant is
        /// wired — an unmatched set loud-warns and falls back to the first wired
        /// conversation (Law 3: the check-in must not go mute over an authoring gap).
        /// </summary>
        public ConversationSO Select(int improved, int decayed, int daysLeft)
        {
            ConversationSO fallback = null;

            foreach (var variant in variants)
            {
                if (variant == null || variant.conversation == null) continue;
                if (fallback == null) fallback = variant.conversation;
                if (variant.Matches(improved, decayed, daysLeft)) return variant.conversation;
            }

            if (fallback == null)
            {
                Debug.LogError($"{name}: no variant has a ConversationSO wired — the check-in has nothing to say. Author at least one variant.", this);
                return null;
            }

            Debug.LogWarning($"{name}: no variant matched (improved={improved}, decayed={decayed}, daysLeft={daysLeft}) — falling back to the first wired conversation. Add an 'Any' catch-all variant at the bottom.", this);
            return fallback;
        }
    }
}
