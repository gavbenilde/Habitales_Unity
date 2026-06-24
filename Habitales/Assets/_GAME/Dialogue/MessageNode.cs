using System;
using UnityEngine;

namespace Habitales.Dialogue
{
    // One authored message in a conversation thread.
    [Serializable]
    public class MessageNode
    {
        public DialogueSpeaker sender;

        // Portrait expression — only meaningful (and only drawn by the editor) when
        // sender is Azi or Bob. Resolved against that character's CharacterProfileSO.
        public string expressionId;

        // Content | Sticker, plus Choice when sender == Player.
        [SerializeReference] public MessagePayload payload;
    }
}
