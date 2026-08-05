using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIButtonTween : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerUpHandler,
    IPointerDownHandler
{
    
    [Header("Hover")]
    public float hoverScale = 1.1f;
    public float hoverDuration = 0.15f;

    [Header("Click")]
    public float clickScale = 0.9f;
    public float clickDuration = 0.08f;

    private Button buttonObject;
    private Vector3 originalScale;
    private bool isHovered;

    void Awake()
    {
        originalScale = transform.localScale;
        buttonObject = GetComponent<Button>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (buttonObject != null && !buttonObject.interactable)
            return;
        
        isHovered = true;

        LeanTween.cancel(gameObject);
        LeanTween.scale(gameObject, originalScale * hoverScale, hoverDuration)
            .setEaseOutQuad()
            .setIgnoreTimeScale(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (buttonObject != null && !buttonObject.interactable)
            return;
        
        isHovered = false;

        LeanTween.cancel(gameObject);
        LeanTween.scale(gameObject, originalScale, hoverDuration)
            .setEaseOutQuad()
            .setIgnoreTimeScale(true);
    }
    
    public void OnPointerUp(PointerEventData eventData)
    {
        if (buttonObject != null && !buttonObject.interactable)
            return;
        
        LeanTween.cancel(gameObject);
        LeanTween.scale(gameObject, originalScale * 0.94f, 0.05f)
            .setIgnoreTimeScale(true);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (buttonObject != null && !buttonObject.interactable)
            return;
        
        Vector3 target = isHovered ? originalScale * hoverScale : originalScale;
        
        LeanTween.cancel(gameObject);
        LeanTween.scale(gameObject, target, 0.12f).setEaseOutBack()
            .setIgnoreTimeScale(true);
    }
}