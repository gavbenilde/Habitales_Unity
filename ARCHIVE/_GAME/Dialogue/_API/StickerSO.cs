using UnityEngine;

namespace Habitales.Dialogue
{
    [CreateAssetMenu(fileName = "Sticker_New", menuName = "Habitales/Dialogue/Sticker")]
    public class StickerSO : ScriptableObject
    {
        [Header("Identity")]
        public string stickerID;

        [Header("Visual")]
        public Sprite sprite;

        [Header("Flags")]
        public bool isBirthdaySticker = false;
    }
}