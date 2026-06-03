using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// One row in the Chat App's tab list.
    /// Displays portrait, name, last message preview, unread dot, and birthday indicator.
    /// </summary>
    public class TabRowUI : MonoBehaviour
    {
        [SerializeField] private Button          rowButton;
        [SerializeField] private Image           portraitImage;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI previewText;
        [SerializeField] private GameObject      unreadDot;
        [SerializeField] private GameObject      birthdayIndicator; // cake icon, ribbon, etc.

        public string TabID { get; private set; }

        public void Setup(TabPreview preview, Action<string> onClickCallback)
        {
            TabID = preview.tabID;

            portraitImage.sprite  = preview.portrait;
            portraitImage.enabled = preview.portrait != null;
            nameText.text         = preview.displayName;
            previewText.text      = preview.lastMessageBody;

            unreadDot.SetActive(preview.hasUnread);

            if (birthdayIndicator != null)
                birthdayIndicator.SetActive(preview.isBirthday);

            rowButton.onClick.RemoveAllListeners();
            rowButton.onClick.AddListener(() => onClickCallback(TabID));
        }

        /// <summary>
        /// Called by ChatAppUI.HandleUnreadChanged to refresh only the dot,
        /// without rebuilding the whole row.
        /// </summary>
        public void RefreshUnreadDot()
        {
            if (unreadDot == null) return;
            var previews = DialogueManager.Instance.GetTabPreviews();
            var updated  = previews.Find(p => p.tabID == TabID);
            unreadDot.SetActive(updated?.hasUnread ?? false);
        }
    }
}