using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Component for category buttons with action count badges.
/// Attach to each category button in the CategoryIconsPanel.
/// </summary>
public class CategoryButton : MonoBehaviour
{
    [Header("References")]
    public Button button;
    public Image icon;
    public TextMeshProUGUI label;
    
    [Header("Badge")]
    public GameObject badge;
    public TextMeshProUGUI badgeText;
    
    void Awake()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }
    }
    
    public void SetBadgeCount(int count)
    {
        if (badge != null)
        {
            badge.SetActive(count > 0);
        }
        
        if (badgeText != null)
        {
            badgeText.text = count.ToString();
        }
        
        // Optionally disable button if no actions
        if (button != null)
        {
            button.interactable = (count > 0);
        }
    }
    
    public void SetIcon(Sprite iconSprite)
    {
        if (icon != null)
        {
            icon.sprite = iconSprite;
        }
    }
}