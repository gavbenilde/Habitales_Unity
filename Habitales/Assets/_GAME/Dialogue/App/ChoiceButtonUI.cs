using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// One selectable option in the chat's choice row. Passive view (Law 1):
    /// receives a resolved ChoiceOptionView and reports the picked index back.
    /// A choice option's label is either text (Content) or a sticker.
    /// </summary>
    public class ChoiceButtonUI : MonoBehaviour
    {
        [SerializeField] private Button          button;
        [SerializeField] private TextMeshProUGUI labelText;    // shown for content options
        [SerializeField] private Image           stickerImage; // shown for sticker options; may be null

        public void Setup(ChoiceOptionView option, int index, Action<int> onSelected)
        {
            bool isSticker = option.isSticker;

            if (labelText != null)
            {
                labelText.gameObject.SetActive(!isSticker);
                labelText.text = option.label;
            }

            if (stickerImage != null)
            {
                stickerImage.gameObject.SetActive(isSticker);
                stickerImage.sprite  = option.stickerSprite;
                stickerImage.enabled = option.stickerSprite != null;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onSelected(index));
        }
    }
}
