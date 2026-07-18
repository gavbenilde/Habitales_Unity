using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// DORMANT (2026-07-18): level-ups cut — kept for reference. No longer instantiated or
// wired anywhere (MainMenu's level-up overlay path was removed). Retained in case the
// level-up flow is revived later.
public class LevelUpScreenUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("XP Bar")]
    // RectTransform pivot must be (0, 0.5) so scale.x grows from the left edge.
    [SerializeField] private RectTransform   xpBarFill;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI xpText;
    [SerializeField] private float           fillDuration = 1.5f;

    [Header("Level-Up Popup")]
    [SerializeField] private GameObject levelUpPopup;
    [SerializeField] private AudioClip  levelUpSfx;

    [Header("Unlocks")]
    [SerializeField] private Transform  unlockScrollContent;
    [SerializeField] private GameObject unlockBoxPrefab;
    [SerializeField] private float      unlockWhitening = 0.4f;

    [Header("Footer")]
    [SerializeField] private Button continueButton;

    private AudioSource _audio;

    void Awake()
    {
        _audio = GetComponent<AudioSource>();
        root.SetActive(false);
        if (levelUpPopup != null) levelUpPopup.SetActive(false);
    }

    public void Show(int xpBefore, int xpEarned, PlayerProgressionSO progression, Action onContinue)
    {
        root.SetActive(true);
        continueButton.gameObject.SetActive(false);
        continueButton.onClick.RemoveAllListeners();
        continueButton.onClick.AddListener(() => { root.SetActive(false); onContinue?.Invoke(); });

        ClearUnlockScroll();

        if (xpEarned == 0)
        {
            levelText.text = $"Level {progression.level}";
            xpText.text    = "0 XP this run — try again!";
            // No XP earned: show the bar at the player's current in-level progress.
            int costNow = progression.XpNeededForNextLevel;
            SetFill(costNow > 0 ? (float)progression.currentLevelXp / costNow : 0f);
            continueButton.gameObject.SetActive(true);
            return;
        }

        StartCoroutine(AnimateFill(xpBefore, xpEarned, progression));
    }

    private IEnumerator AnimateFill(int xpBefore, int xpEarned, PlayerProgressionSO progression)
    {
        // Per-level XP semantics: AddXp has already advanced progression.level and
        // progression.currentLevelXp. We replay the fill in segments: start from the
        // pre-run level/XP, fill toward each per-level threshold, pop a burst on
        // overflow, reset to 0, continue. Reconstruct the pre-run state by walking
        // backwards from current.
        int finalLevel        = progression.level;
        int finalCurrentXp    = progression.currentLevelXp;

        // Pre-run currentLevelXp is what was already on the bar before the run added xpEarned.
        // We don't store it directly, so reconstruct: simulate forward from
        // (displayLevel = computed pre-run level, segXp = xpBefore-equivalent).
        // Simpler: rewind by xpEarned across the per-level thresholds.
        int displayLevel = finalLevel;
        int segXp        = finalCurrentXp;
        int remainingRewind = xpEarned;
        while (remainingRewind > 0)
        {
            if (segXp >= remainingRewind) { segXp -= remainingRewind; remainingRewind = 0; }
            else
            {
                remainingRewind -= segXp;
                displayLevel = Mathf.Max(1, displayLevel - 1);
                int prevCost = progression.levelThresholds[Mathf.Clamp(displayLevel, 1, progression.levelThresholds.Length - 1)];
                segXp = prevCost; // before the level-up, bar was at the cap
                // Then back-off the remaining
                int chunk = Mathf.Min(segXp, remainingRewind);
                segXp -= chunk;
                remainingRewind -= chunk;
                if (displayLevel == 1 && remainingRewind > 0) { remainingRewind = 0; } // clamp
            }
        }

        levelText.text = $"Level {displayLevel}";

        int xpLeftToAnimate = xpEarned;
        while (xpLeftToAnimate > 0)
        {
            int segCost = progression.levelThresholds[Mathf.Clamp(displayLevel, 1, progression.levelThresholds.Length - 1)];
            int xpToFinishSeg = segCost - segXp;
            int fillAmount    = Mathf.Min(xpToFinishSeg, xpLeftToAnimate);

            float fillStart = (float)segXp / Mathf.Max(1, segCost);
            float fillEnd   = (float)(segXp + fillAmount) / Mathf.Max(1, segCost);

            float segDuration = Mathf.Max(0.1f, fillDuration * (fillEnd - fillStart));
            float elapsed     = 0f;
            int startSegXp = segXp;
            while (elapsed < segDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / segDuration);
                SetFill(Mathf.Lerp(fillStart, fillEnd, t));
                int displayXp = startSegXp + Mathf.RoundToInt(fillAmount * t);
                xpText.text = $"{displayXp} / {segCost} XP";
                yield return null;
            }

            SetFill(fillEnd);
            segXp           += fillAmount;
            xpLeftToAnimate -= fillAmount;

            bool atMaxLevel = displayLevel >= progression.levelThresholds.Length - 1;
            if (segXp >= segCost && xpLeftToAnimate > 0 && !atMaxLevel)
            {
                displayLevel++;
                segXp = 0;
                yield return LevelUpBurst(displayLevel);
                SetFill(0f);
            }
            else if (atMaxLevel && xpLeftToAnimate > 0)
            {
                // Stop animating once at max level — overflow XP is capped.
                xpLeftToAnimate = 0;
            }
        }

        levelText.text = $"Level {progression.level}";
        xpText.text    = $"{progression.currentLevelXp} / {progression.XpNeededForNextLevel} XP";

        int startIdx = Mathf.Clamp(progression.lastSeenUnlockCount, 0, progression.unlockedPlantIds.Count);
        for (int i = startIdx; i < progression.unlockedPlantIds.Count; i++)
        {
            string id   = progression.unlockedPlantIds[i];
            string name = i < progression.unlockedPlantNames.Count ? progression.unlockedPlantNames[i] : null;
            SpawnUnlockBox(id, name);
        }

        continueButton.gameObject.SetActive(true);
    }

    private IEnumerator LevelUpBurst(int newLevel)
    {
        levelText.text = $"Level {newLevel}";
        if (levelUpPopup != null) levelUpPopup.SetActive(true);
        if (_audio != null && levelUpSfx != null) _audio.PlayOneShot(levelUpSfx);
        yield return new WaitForSeconds(1f);
        if (levelUpPopup != null) levelUpPopup.SetActive(false);
    }

    private void SetFill(float t)
    {
        if (xpBarFill == null) return;
        var s = xpBarFill.localScale;
        s.x = Mathf.Clamp01(t);
        xpBarFill.localScale = s;
    }

    private float NormalizedXp(int xp, PlayerProgressionSO p)
    {
        int segStart = p.levelThresholds[Mathf.Clamp(p.level - 1, 0, p.levelThresholds.Length - 1)];
        int segEnd   = p.level < p.levelThresholds.Length
            ? p.levelThresholds[p.level]
            : segStart + 1;
        return Mathf.Clamp01((float)(xp - segStart) / Mathf.Max(1, segEnd - segStart));
    }

    private void ClearUnlockScroll()
    {
        if (unlockScrollContent == null) return;
        for (int i = unlockScrollContent.childCount - 1; i >= 0; i--)
            Destroy(unlockScrollContent.GetChild(i).gameObject);
    }

    private void SpawnUnlockBox(string profileId, string plantName)
    {
        if (unlockScrollContent == null || unlockBoxPrefab == null) return;

        GameObject box = Instantiate(unlockBoxPrefab, unlockScrollContent);
        box.name = $"Unlock_{plantName}";

        // Cube-era per-plant color (GeneratedPlantRegistry + HWBColor) is gone; unlock boxes
        // render with a neutral fill. In Alpha, XP unlocks nothing functional anyway.
        Color color = Color.gray;

        Image bg = box.GetComponent<Image>();
        if (bg != null) bg.color = color;

        TMP_Text label = box.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text  = string.IsNullOrEmpty(plantName) ? "New plant!" : plantName;
            label.color = Color.black;
        }
    }
}
