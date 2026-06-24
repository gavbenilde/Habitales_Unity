using System;
using System.Collections.Generic;
using UnityEngine;

namespace Habitales.Dialogue
{
    // Portrait expression entry for a fixed character (Azi / Bob). Relocated here from
    // the deleted DialogueTypes.cs so it survives the per-thread cast removal.
    [Serializable]
    public class ExpressionEntry
    {
        public string expressionID;
        public Sprite sprite;
    }

    // Homes a fixed character's portrait + expression set, replacing the old per-thread
    // SpeakerProfile cast. One asset per character (Azi, Bob). The conversation editor
    // populates a message's expression dropdown from the matching character's profile.
    [CreateAssetMenu(fileName = "Character_New", menuName = "Habitales/Dialogue/Character Profile")]
    public class CharacterProfileSO : ScriptableObject
    {
        [Header("Identity")]
        public DialogueSpeaker character;   // Azi or Bob
        public string displayName;

        [Header("Visual")]
        public Sprite portrait;
        public List<ExpressionEntry> expressions = new List<ExpressionEntry>();

        public Sprite GetExpression(string expressionID)
        {
            if (string.IsNullOrEmpty(expressionID)) return portrait;
            var e = expressions.Find(x => x.expressionID == expressionID);
            return e != null && e.sprite != null ? e.sprite : portrait;
        }
    }
}
