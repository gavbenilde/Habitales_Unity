using UnityEngine;

namespace Habitales.Dialogue
{
    [CreateAssetMenu(fileName = "Tab_New", menuName = "Habitales/Dialogue/Dialogue Tab")]
    public class DialogueTabSO : ScriptableObject
    {
        [Header("Identity")]
        public string tabID;
        public string displayName;

        [Header("Visual")]
        public Sprite tabPortrait;
        public bool isGroupChat = false;
    }
}