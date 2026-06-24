using System.Collections.Generic;
using UnityEngine;

namespace Habitales.Dialogue
{
    // A single authored conversation. Replaces DialogueThreadSO / WorkerMessageTemplateSO.
    // No threadID/tabID strings — routing is the channel enum, identity is the asset name.
    [CreateAssetMenu(fileName = "Conversation_New", menuName = "Habitales/Dialogue/Conversation")]
    public class ConversationSO : ScriptableObject
    {
        [Header("Trigger")]
        public DialogueTrigger trigger = DialogueTrigger.DailyRoll;

        [Header("Routing")]
        public DialogueChannel channel = DialogueChannel.GroupChat;

        // Personality pool — only meaningful when channel == Worker. The custom editor
        // shows a single dropdown of the 10 WorkerTraits plus "Universal"; selecting
        // Universal sets the bool below. A worker draws from (its trait) ∪ (universal).
        public WorkerTrait personality;
        public bool universal;

        [Header("Thread")]
        public List<MessageNode> thread = new List<MessageNode>();
    }
}
