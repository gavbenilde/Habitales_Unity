using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BooglePanelUI : MonoBehaviour
{
    public static BooglePanelUI Instance { get; private set; }

    [SerializeField] private GameObject overlayBlocker;
    [SerializeField] private GameObject card;
    [SerializeField] private TMP_Text plantNameText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Button closeButton;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
        closeButton?.onClick.AddListener(Hide);
    }

    public void Show(PlantingProfileSO profile)
    {
        plantNameText.text = profile.plantName;
        bodyText.text      = $"Specialty: {profile.SpecialtyStatName}\n\nThis plant can survive extreme {profile.SpecialtyStatName}.";
        overlayBlocker.SetActive(true);
        card.SetActive(true);
    }

    public void Hide()
    {
        overlayBlocker.SetActive(false);
        card.SetActive(false);
    }
}
