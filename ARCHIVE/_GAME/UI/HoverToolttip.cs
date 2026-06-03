using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class HoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private GameObject tooltipPanel;
    [SerializeField] private TextMeshProUGUI tooltipText;

    private string message;

    /// <summary>
    /// Called by ActionUI at runtime to inject the panel, text, and message.
    /// </summary>
    public void Configure(GameObject panel, TextMeshProUGUI text, string msg)
    {
        tooltipPanel = panel;
        tooltipText  = text;
        message      = msg;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tooltipPanel == null) return;
        if (tooltipText  != null) tooltipText.text = message;
        tooltipPanel.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltipPanel != null)
            tooltipPanel.SetActive(false);
    }

    void OnDisable()
    {
        // Safety: hide tooltip if the icon is hidden mid-hover
        if (tooltipPanel != null)
            tooltipPanel.SetActive(false);
    }
}