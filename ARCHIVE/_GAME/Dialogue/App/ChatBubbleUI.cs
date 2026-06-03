using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// One message bubble in the thread view.
    /// Used by both the NPC prefab (left-aligned, has portrait + name)
    /// and the Player prefab (right-aligned, no portrait).
    /// Assign only the fields relevant to each prefab variant — null fields are safely skipped.
    /// </summary>
    public class ChatBubbleUI : MonoBehaviour
    {
        [Header("Text Bubble")]
        [SerializeField] private GameObject      textBubbleRoot;
        [SerializeField] private TextMeshProUGUI bodyText;
        [SerializeField] private TextMeshProUGUI senderNameText; // leave null on player prefab

        [Header("Portrait (NPC prefab only)")]
        [SerializeField] private Image portraitImage; // leave null on player prefab

        [Header("Sticker Bubble")]
        [SerializeField] private GameObject stickerBubbleRoot;
        [SerializeField] private Image      stickerImage;

        public void Setup(ResolvedLine line)
        {
            bool isSticker = line.isStickerBubble;

            textBubbleRoot.SetActive(!isSticker);
            stickerBubbleRoot.SetActive(isSticker);

            if (isSticker)
            {
                stickerImage.sprite  = line.stickerSprite;
                stickerImage.enabled = line.stickerSprite != null;
            }
            else
            {
                bodyText.text = line.body;

                if (senderNameText != null)
                    senderNameText.text = line.displayName;

                if (portraitImage != null)
                {
                    portraitImage.sprite  = line.portrait;
                    portraitImage.enabled = line.portrait != null;
                }
            }
        }
    }
}