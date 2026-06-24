using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// BooglePanelUI — the standalone "?" lookup overlay (prototype CLAUDE.md §6.8 / §9).
// Now driven by ActionSO Boogle data surfaced through PlayerAction (displayName + lore +
// infoTooltip + supplementaryImages) instead of the old PlantingProfileSO. Still a
// lightweight scene-scoped singleton so it can be re-parented under the Tablet UI shell
// post-prototype without a refactor.
public class BooglePanelUI : MonoBehaviour
{
    public static BooglePanelUI Instance { get; private set; }

    [SerializeField] private GameObject overlayBlocker;
    [SerializeField] private GameObject card;
    [SerializeField] private TMP_Text plantNameText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Button closeButton;

    // Optional — only the first supplementary image is shown. Null-safe: if the prefab has
    // no image element wired, the panel still works as text-only.
    [SerializeField] private Image supplementaryImage;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
        closeButton?.onClick.AddListener(Hide);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Opens the panel for an action, showing its authored Boogle encyclopedia data.
    /// </summary>
    public void Show(PlayerAction action)
    {
        if (action == null) { Debug.LogWarning("BooglePanelUI.Show called with null action."); return; }

        if (plantNameText != null)
            plantNameText.text = action.ActionName;

        if (bodyText != null)
        {
            // Lore reads as the main blurb; infoTooltip is a short gameplay note beneath it.
            // Fall back to the action description if no Boogle text was authored.
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(action.Lore))        parts.Add(action.Lore.Trim());
            if (!string.IsNullOrWhiteSpace(action.InfoTooltip)) parts.Add(action.InfoTooltip.Trim());
            if (parts.Count == 0 && !string.IsNullOrWhiteSpace(action.Description))
                parts.Add(action.Description.Trim());

            bodyText.text = string.Join("\n\n", parts);
        }

        if (supplementaryImage != null)
        {
            IReadOnlyList<Sprite> images = action.SupplementaryImages;
            Sprite first = (images != null && images.Count > 0) ? images[0] : null;
            supplementaryImage.sprite  = first;
            supplementaryImage.enabled = first != null;
        }

        if (overlayBlocker != null) overlayBlocker.SetActive(true);
        if (card != null)           card.SetActive(true);
    }

    public void Hide()
    {
        if (overlayBlocker != null) overlayBlocker.SetActive(false);
        if (card != null)           card.SetActive(false);
    }
}
