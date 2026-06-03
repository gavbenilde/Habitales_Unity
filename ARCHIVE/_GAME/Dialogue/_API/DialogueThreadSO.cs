using System.Collections.Generic;
using UnityEngine;

namespace Habitales.Dialogue
{
    [CreateAssetMenu(fileName = "Thread_New", menuName = "Habitales/Dialogue/Dialogue Thread")]
    public class DialogueThreadSO : ScriptableObject
    {
        [Header("Identity")]
        public string threadID;
        public string tabID;
        public SenderResolution senderResolution = SenderResolution.Fixed;

        [Header("Cast")]
        public List<SpeakerProfile> cast = new List<SpeakerProfile>();

        [Header("Lines")]
        public List<DialogueLine> lines = new List<DialogueLine>();

        public SpeakerProfile GetSpeaker(string speakerID)
        {
            return cast.Find(p => p.speakerID == speakerID);
        }
    }
}