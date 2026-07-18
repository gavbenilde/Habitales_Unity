using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Habitales.UI.Actions
{
    /// <summary>
    /// Passive view: owns card building for the action strip. Instantiates ActionCardView prefabs,
    /// tracks them in a dictionary, and raises events upward when a card is clicked. The controller
    /// drives visibility and highlight calls.
    /// </summary>
    public class ActionStripView : MonoBehaviour
    {
        [Tooltip("The panel toggled open/closed (background + content). Falls back to content.gameObject if null.")]
        [SerializeField] private GameObject stripRoot;
        [SerializeField] private Transform content;
        [SerializeField] private GameObject cardPrefab;
        [SerializeField] private Color defaultCardColor = new Color(0.75f, 0.75f, 0.75f);

        /// <summary>Raised when the player clicks an action card. Law-2: on the click.</summary>
        public event Action<PlayerAction> OnActionCardClicked;

        private readonly Dictionary<PlayerAction, GameObject> cardObjects = new Dictionary<PlayerAction, GameObject>();

        // ─── Strip visibility ─────────────────────────────────────────────────

        /// <summary>Shows or hides the strip panel root.</summary>
        public void SetVisible(bool visible)
        {
            var target = StripToggleTarget;
            if (target != null) target.SetActive(visible);
        }

        /// <summary>True when the strip panel root is active.</summary>
        public bool IsVisible => StripToggleTarget != null && StripToggleTarget.activeSelf;

        private GameObject StripToggleTarget =>
            stripRoot != null ? stripRoot :
            (content != null ? content.gameObject : null);

        // ─── Card building ────────────────────────────────────────────────────

        /// <summary>
        /// Clears all existing cards and rebuilds the strip from <paramref name="actions"/>.
        /// </summary>
        public void Render(IEnumerable<PlayerAction> actions)
        {
            // Clear previous cards.
            cardObjects.Clear();
            if (content != null)
            {
                foreach (Transform child in content)
                    Destroy(child.gameObject);
            }

            if (actions == null) return;

            foreach (PlayerAction action in actions)
            {
                SpawnActionCard(action);
            }
        }

        private void SpawnActionCard(PlayerAction action)
        {
            if (cardPrefab == null || content == null) return;

            GameObject card = Instantiate(cardPrefab, content);
            card.name = $"Card_{action.ActionName}";

            var view = card.GetComponent<ActionCardView>();
            if (view == null)
            {
                Debug.LogError($"{name}: action card prefab is missing an ActionCardView component — add it and wire its refs.", this);
                return;
            }

            cardObjects[action] = card;

            // Capture locals so the closure doesn't close over a mutating loop variable.
            PlayerAction captured = action;
            view.Bind(
                captured.ActionName,
                captured.Icon,      // ActionSO.icon via GenericPlayerAction; null hides the icon
                defaultCardColor,
                () => OnActionCardClicked?.Invoke(captured),
                () => OpenBoogle(captured));
        }

        private void OpenBoogle(PlayerAction action)
        {
            if (BooglePanelUI.Instance != null)
                BooglePanelUI.Instance.Show(action);
            else
                Debug.LogWarning("[Boogle] Instance is null — BooglePanel root is inactive or absent in the scene.");
        }

        // ─── Highlight ────────────────────────────────────────────────────────

        /// <summary>
        /// Selects the card button for <paramref name="active"/> and fades all others back to
        /// normal colour. Pass null to clear all highlights (e.g. on Disarm).
        /// </summary>
        public void Highlight(PlayerAction active)
        {
            foreach (var kvp in cardObjects)
            {
                if (kvp.Value == null) continue;
                Button btn = kvp.Value.GetComponent<Button>();
                if (btn == null) continue;

                if (kvp.Key == active)
                {
                    btn.Select();
                }
                else
                {
                    btn.targetGraphic?.CrossFadeColor(btn.colors.normalColor, 0f, true, true);
                }
            }
        }

        // ─── Rect resolution ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the RectTransform of the card for <paramref name="action"/>, or null if the
        /// card has not been built yet (strip collapsed or category not selected).
        /// </summary>
        public RectTransform GetCardRect(PlayerAction action)
        {
            if (action == null) return null;
            if (cardObjects.TryGetValue(action, out GameObject card) && card != null)
                return card.transform as RectTransform;
            return null;
        }
    }
}
