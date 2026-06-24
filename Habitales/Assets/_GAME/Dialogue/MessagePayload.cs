using System;
using System.Collections.Generic;
using UnityEngine;

namespace Habitales.Dialogue
{
    // Polymorphic message body. Stored via [SerializeReference] on MessageNode /
    // ChoiceOption so a single field can hold Content, Sticker, or Choice and the
    // custom editor can swap the concrete type from a dropdown.
    [Serializable]
    public abstract class MessagePayload { }

    [Serializable]
    public class ContentPayload : MessagePayload
    {
        [TextArea(2, 6)] public string body;
    }

    [Serializable]
    public class StickerPayload : MessagePayload
    {
        public StickerSO sticker;
    }

    // Only valid on a Player message node. Variable option count (1..N).
    [Serializable]
    public class ChoicePayload : MessagePayload
    {
        public List<ChoiceOption> options = new List<ChoiceOption>();
    }

    // One selectable option. 'label' is the bubble shown as the player's reply once
    // chosen (Content or Sticker). 'children' is the option's sub-thread; when empty,
    // flow falls straight through to the parent's next node (branch merge).
    [Serializable]
    public class ChoiceOption
    {
        [SerializeReference] public MessagePayload label;          // ContentPayload or StickerPayload
        public List<MessageNode> children = new List<MessageNode>();
    }
}
