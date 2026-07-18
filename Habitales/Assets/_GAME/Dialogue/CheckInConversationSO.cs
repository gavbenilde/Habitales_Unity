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
    /// ConversationEditor; set its Trigger to Manual — the check-in panel plays the
    /// thread directly, nothing is delivered to a chat tab.
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
    /// The specialized check-in dialogue asset: a pool of variants. At check-in time,
    /// Select() gathers every variant whose conditions match the player's situation and
    /// picks ONE at random — list order carries no priority. An Any variant matches every
    /// situation, so it competes in every draw alongside the specific variants.
    ///
    /// Variant bodies may use the tokens {days}, {improved}, {decayed} — CheckInPanelUI sets
    /// them as EventContext overrides before resolving, so DialogueManager's normal
    /// EventContext.Resolve pass substitutes them (plus any global tokens) for free.
    ///
    /// AUTHORING (human):
    ///   1. Create one ConversationSO per tone (e.g. Conversation_CheckIn_Improving,
    ///      Conversation_CheckIn_Decaying, Conversation_CheckIn_Balanced) with the normal
    ///      conversation editor, with Trigger set to Manual.
    ///   2. Create this asset (Habitales/Dialogue/Check-In Conversation), add one variant per
    ///      tone, set conditions, and drag each ConversationSO in.
    ///   3. Include at least one tileTrend = Any variant with no days-left limit so every
    ///      situation has something to draw from. Select() loud-warns and falls back to a
    ///      random wired conversation if nothing matches.
    /// </summary>
    [CreateAssetMenu(fileName = "CheckIn_New", menuName = "Habitales/Dialogue/Check-In Conversation")]
    public class CheckInConversationSO : ScriptableObject
    {
        public List<CheckInVariant> variants = new List<CheckInVariant>();

        /// <summary>
        /// Picks the conversation to play for this check-in: a uniform random draw from
        /// every variant whose conditions match. Never silently returns null while any
        /// variant is wired — if nothing matches, it loud-warns and draws from ALL wired
        /// variants instead (Law 3: the check-in must not go mute over an authoring gap).
        /// </summary>
        public ConversationSO Select(int improved, int decayed, int daysLeft)
        {
            var wired   = new List<ConversationSO>();
            var matches = new List<ConversationSO>();

            foreach (var variant in variants)
            {
                if (variant == null || variant.conversation == null) continue;
                wired.Add(variant.conversation);
                if (variant.Matches(improved, decayed, daysLeft)) matches.Add(variant.conversation);
            }

            if (matches.Count > 0)
                return matches[UnityEngine.Random.Range(0, matches.Count)];

            if (wired.Count == 0)
            {
                Debug.LogError($"{name}: no variant has a ConversationSO wired — the check-in has nothing to say. Author at least one variant.", this);
                return null;
            }

            Debug.LogWarning($"{name}: no variant matched (improved={improved}, decayed={decayed}, daysLeft={daysLeft}) — drawing from all wired variants instead. Add an 'Any' variant so every situation has a match.", this);
            return wired[UnityEngine.Random.Range(0, wired.Count)];
        }
    }
}
