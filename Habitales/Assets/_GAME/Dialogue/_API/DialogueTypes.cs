using System;
using System.Collections.Generic;
using UnityEngine;

namespace Habitales.Dialogue
{
    public enum SenderResolution
    {
        Fixed,
        RandomWorker
    }

    [Serializable]
    public class ExpressionEntry
    {
        public string expressionID;
        public Sprite sprite;
    }

    [Serializable]
    public class SpeakerProfile
    {
        public string speakerID;
        public string displayName;
        public Sprite staticPortrait;
        public List<ExpressionEntry> expressions = new List<ExpressionEntry>();

        public Sprite GetExpression(string expressionID)
        {
            if (string.IsNullOrEmpty(expressionID)) return staticPortrait;
            var entry = expressions.Find(e => e.expressionID == expressionID);
            return entry?.sprite != null ? entry.sprite : staticPortrait;
        }
    }

    [Serializable]
    public class DialogueLine
    {
        public string speakerID;
        [TextArea(2, 6)]
        public string body;
        public string expressionID;
    }
}