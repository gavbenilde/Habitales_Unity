using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIHoverLift : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [SerializeField] private float liftAmount = 15f;
    [SerializeField] private float duration = 0.15f;
    
    private Button buttonObject;
    private CanvasGroup canvasGroup;
    
    private RectTransform rectTransform;
    private Vector2 originalPosition;

    private bool initialized;
    private bool isHovered;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        buttonObject = GetComponent<Button>();
        canvasGroup = GetComponent<CanvasGroup>();
    }
    
    private IEnumerator Start()
    {
        yield return null;

        originalPosition = rectTransform.anchoredPosition;
        initialized = true;
        
        // if hovered after init, call hover
        if (isHovered)
        {
            Hover();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!CanInteract())
            return;
        
        isHovered = true;
        
        FMODUnity.EventReference ev = FMODEvents.instance.cardHoverEnter;
            
        float randomPitch = Random.Range(0.9f, 1.1f);

        AudioManager.instance.PlayOneShot(ev, Vector3.zero, randomPitch);
        

        if (!initialized)
            return;

        Hover();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;

        if (!initialized)
            return;

        // FMODUnity.EventReference ev = FMODEvents.instance.cardHoverExit;
        //     
        // float randomPitch = Random.Range(0.9f, 1.1f);
        //
        // AudioManager.instance.PlayOneShot(ev, Vector3.zero, randomPitch);
        
        LeanTween.cancel(gameObject);

        LeanTween.moveY(
                rectTransform,
                originalPosition.y,
                duration)
            .setEaseOutQuad();
    }

    private void Hover()
    {
        if (!CanInteract())
            return;
        
        LeanTween.cancel(gameObject);

        LeanTween.moveY(
                rectTransform,
                originalPosition.y + liftAmount,
                duration)
            .setEaseOutQuad();
    }
    
    private bool CanInteract()
    {
        return canvasGroup == null || canvasGroup.interactable;
    }
}