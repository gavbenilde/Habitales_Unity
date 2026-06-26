using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Habitales.UI.Actions
{

/// <summary>
/// Self-describing view for a single action card. Lives on the action-card prefab and
/// holds its own widget references so the spawner never reaches into the hierarchy by string.
/// </summary>
public class ActionCardView : MonoBehaviour
{
    [SerializeField] private Image    background;   // the card root Image (background tint)
    [SerializeField] private Button   cardButton;   // the card root Button (arm / lock click)
    [SerializeField] private Image    icon;         // "ActionIcon"
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button   boogleButton; // the "?" button (optional per card)

    /// <summary>Renders this card and wires its click handlers. Pass iconSprite null to hide the icon;
    /// pass onBoogle null to disable/hide the "?" button (e.g. the lock card).</summary>
    public void Bind(string displayName, Sprite iconSprite, Color bgColor, Action onClick, Action onBoogle)
    {
        if (background != null) background.color = bgColor;

        if (label != null) { label.text = displayName; label.color = Color.black; }

        if (icon != null)
        {
            bool show = iconSprite != null;
            if (show) icon.sprite = iconSprite;
            icon.gameObject.SetActive(show);
        }

        if (cardButton != null)
        {
            cardButton.onClick.RemoveAllListeners();
            if (onClick != null) cardButton.onClick.AddListener(() => onClick());
        }

        if (boogleButton != null)
        {
            boogleButton.onClick.RemoveAllListeners();
            if (onBoogle != null) boogleButton.onClick.AddListener(() => onBoogle());
            boogleButton.gameObject.SetActive(onBoogle != null);
        }
    }
}

} // namespace Habitales.UI.Actions
