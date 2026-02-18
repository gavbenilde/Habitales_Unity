using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OverflowTip : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI tipText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Animation")]
    [SerializeField] private float displayDuration = 0.8f;  // Hold before fade
    [SerializeField] private float fadeDuration    = 0.7f;  // Fade-out duration
    [SerializeField] private float driftSpeed      = 40f;   // Upward drift in pixels/sec

    public void Initialize(string message)
    {
        if (tipText    != null) tipText.text   = message;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        StartCoroutine(FadeAndDestroy());
    }

    private IEnumerator FadeAndDestroy()
    {
        float elapsed = 0f;

        // Hold phase — drift upward at full opacity
        while (elapsed < displayDuration)
        {
            elapsed += Time.deltaTime;
            transform.position += Vector3.up * driftSpeed * Time.deltaTime;
            yield return null;
        }

        // Fade phase — continue drifting while fading out
        elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            transform.position += Vector3.up * driftSpeed * Time.deltaTime;
            yield return null;
        }

        Destroy(gameObject);
    }
}