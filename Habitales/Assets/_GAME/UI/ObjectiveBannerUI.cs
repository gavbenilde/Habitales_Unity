using UnityEngine;
using TMPro;

/// <summary>
/// Simple top-of-screen objective banner. Tells the player the goal up front
/// ("Rehabilitate the world!") and — once a zone is ready to unlock — nudges
/// them toward the Unlock button. Reuses RunManager's zone-unlock events.
/// </summary>
public class ObjectiveBannerUI : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private TextMeshProUGUI bannerText;

    [Tooltip("Optional. CanvasGroup on the banner text used for the landing-reveal fade " +
             "(PlayLandingReveal). If left unassigned, one is added at runtime via code — " +
             "the banner is live in-scene, so this is optional-with-warning, not loud-fail.")]
    [SerializeField] private CanvasGroup bannerCanvasGroup;

    [Header("Messages")]
    [SerializeField] private string defaultMessage      = "Rehabilitate the world!";
    [SerializeField] private string unlockReadyMessage  = "Zone restored — unlock the next zone!";

    [Header("Landing Reveal (PlayLandingReveal)")]
    [Tooltip("Starting scale multiplier — the text begins this many times its native size.")]
    [SerializeField] private float landingStartScale     = 4f;
    [Tooltip("Duration of the scale-down + fade-in, before the overshoot settle.")]
    [SerializeField] private float landingDuration        = 0.6f;
    [Tooltip("How far past native scale the overshoot settle punches (1.05 = 5% over).")]
    [SerializeField] private float landingOvershootScale  = 1.05f;
    [Tooltip("Duration of the tiny overshoot-and-settle tail after the main scale/fade.")]
    [SerializeField] private float landingSettleDuration  = 0.15f;

    private RunManager runManager;

    void Awake()
    {
        if (bannerText == null) bannerText = GetComponent<TextMeshProUGUI>();
    }

    void Start()
    {
        SetText(defaultMessage);

        runManager = RunManager.Instance;
        if (runManager != null)
        {
            runManager.OnRegionUnlockReady += HandleUnlockReady;
            runManager.OnRegionUnlocked    += HandleUnlocked;
        }
    }

    void OnDestroy()
    {
        if (runManager != null)
        {
            runManager.OnRegionUnlockReady -= HandleUnlockReady;
            runManager.OnRegionUnlocked    -= HandleUnlocked;
        }
    }

    private void HandleUnlockReady() => SetText(unlockReadyMessage);
    private void HandleUnlocked()    => SetText(defaultMessage);

    private void SetText(string msg)
    {
        if (bannerText != null) bannerText.text = msg;
    }

    // ── Landing reveal (arch ENDGAME_BUILD_PLAN §6.3) ──────────────────────
    //
    // "Rehabilitate the World!" starts gigantic + invisible, LeanTweens down to native
    // banner size while fading to opaque, then punches a tiny overshoot and settles — the
    // "text lands in front of the player" illusion. Director-triggered at graduation
    // (Beat_3_4). Cancel-safe (re-entrant — calling this while a prior play is still running
    // cancels it cleanly first) and idempotent at rest: whether skipped, cancelled mid-flight,
    // or run to completion, the banner always ends at exactly native scale, fully opaque.

    /// <summary>
    /// Plays the "text lands in front of the player" reveal: starts at
    /// <see cref="landingStartScale"/>x native size with zero alpha, scales down to native size
    /// (easeOutCubic) while fading in (easeInQuad) over <see cref="landingDuration"/> seconds,
    /// then punches a tiny overshoot past native scale and settles back over
    /// <see cref="landingSettleDuration"/> seconds. Safe to call when the banner is already
    /// visible (e.g. re-triggered) — cancels any in-flight tween first and always ends at
    /// exactly native scale / alpha 1, never a stuck mid-tween state.
    /// </summary>
    public void PlayLandingReveal()
    {
        if (bannerText == null)
        {
            Debug.LogWarning($"{name}: ObjectiveBannerUI.PlayLandingReveal — bannerText is not wired; nothing to animate.", this);
            return;
        }

        Transform t = bannerText.transform;
        CanvasGroup cg = ResolveCanvasGroup();

        // Cancel-safe: stop any tween already running on this target before starting a new one,
        // so re-entrant calls (or a call that lands mid-animation) can't stack/fight tweens.
        LeanTween.cancel(t.gameObject);
        if (cg != null) LeanTween.cancel(cg.gameObject);

        // Start state: gigantic + invisible.
        t.localScale = Vector3.one * landingStartScale;
        if (cg != null) cg.alpha = 0f;

        // Stage 1 — scale down to native size (easeOutCubic) while fading in (easeInQuad).
        LeanTween.scale(t.gameObject, Vector3.one, landingDuration)
            .setEase(LeanTweenType.easeOutCubic)
            .setIgnoreTimeScale(true)
            .setOnComplete(() => PlayOvershootSettle(t, landingOvershootScale, landingSettleDuration));

        if (cg != null)
        {
            LeanTween.alphaCanvas(cg, 1f, landingDuration)
                .setEase(LeanTweenType.easeInQuad)
                .setIgnoreTimeScale(true);
        }
    }

    // Stage 2 — tiny overshoot past native scale, then settle back to exactly native. Idempotent
    // end state: the onComplete forces localScale back to Vector3.one regardless of interruption.
    private void PlayOvershootSettle(Transform t, float overshootScale, float settleDuration)
    {
        if (t == null) return;

        LeanTween.scale(t.gameObject, Vector3.one * overshootScale, settleDuration * 0.5f)
            .setEase(LeanTweenType.easeOutQuad)
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                LeanTween.scale(t.gameObject, Vector3.one, settleDuration * 0.5f)
                    .setEase(LeanTweenType.easeInOutQuad)
                    .setIgnoreTimeScale(true)
                    .setOnComplete(() => t.localScale = Vector3.one); // idempotent end state
            });
    }

    // Optional-with-warning (Law 3, artist-facing but non-fatal — the banner is live in-scene
    // and must not brick if unwired): use the serialized ref if wired, else add a CanvasGroup to
    // the text object in code so the fade half of the reveal still works.
    private CanvasGroup ResolveCanvasGroup()
    {
        if (bannerCanvasGroup != null) return bannerCanvasGroup;

        if (bannerText == null) return null;

        bannerCanvasGroup = bannerText.GetComponent<CanvasGroup>();
        if (bannerCanvasGroup == null)
        {
            bannerCanvasGroup = bannerText.gameObject.AddComponent<CanvasGroup>();
            Debug.LogWarning($"{name}: ObjectiveBannerUI — bannerCanvasGroup was not wired; added one to " +
                              $"'{bannerText.gameObject.name}' at runtime. Wire it in the Inspector to avoid this.", this);
        }
        return bannerCanvasGroup;
    }
}
