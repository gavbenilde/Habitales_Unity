using System;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// One sticker in the sticker tray.
    /// Pass isLocked = true for birthday stickers that have already been sent this year.
    /// </summary>
    public class StickerButtonUI : MonoBehaviour
    {
        [SerializeField] private Button  button;
        [SerializeField] private Image   stickerImage;
        [SerializeField] private GameObject lockedOverlay; // dim/grey overlay; can be null

        public void Setup(StickerSO sticker, Action<StickerSO> onSelected, bool isLocked = false)
        {
            stickerImage.sprite = sticker.sprite;
            button.interactable = !isLocked;

            if (lockedOverlay != null)
                lockedOverlay.SetActive(isLocked);

            button.onClick.RemoveAllListeners();
            if (!isLocked)
                button.onClick.AddListener(() => onSelected(sticker));
        }
    }
}