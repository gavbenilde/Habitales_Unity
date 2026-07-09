using System;
using System.Collections;
using UnityEngine;

public class DayNightCycleHandler : MonoBehaviour
{
    [SerializeField] private GameObject directionalLight;

    private readonly float dayDuration = 5f;   // first day of any action
    private readonly float minDuration = 0.3125f; // floor (~0.31 s)

    private Coroutine currentCycle;
    private int actionDayIndex = 0;
    
    private Quaternion _dayStartRotation;

    // ── Idle gate ────────────────────────────────────────────────────────────
    // ResourceManager.AdvanceTimeStepped yields on this.
    // Fixed: was never set false, so the WaitUntil resolved immediately.
    public static bool IsIdle { get; private set; } = true;

    // Force the gate back to idle. Used by RunRestart: if a restart lands mid-cycle (an action
    // running, an event interrupt, etc.) IsIdle could be latched false with the coroutine that
    // would have flipped it back destroyed by the scene reload — the next run's first
    // AdvanceTimeStepped would then hang forever on WaitUntil(IsIdle). Not used mid-run.
    public static void ForceIdle() => IsIdle = true;

    // Scene-singleton convenience (one handler per scene) — not a manager, no init-order
    // pin. Lets ActionManager resolve it without FindObjectOfType.
    public static DayNightCycleHandler Instance { get; private set; }

    public event Action<int> OnCycleEnd;

    void Awake()
    {
        Instance = this;
        _dayStartRotation = directionalLight.transform.rotation;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        if (ResourceManager.Instance != null)
            ResourceManager.Instance.OnTimeAdvanced += StartCycle;
    }

    void OnDisable()
    {
        if (ResourceManager.Instance != null)
            ResourceManager.Instance.OnTimeAdvanced -= StartCycle;

        TimeFlowSignal.SpeedFactor = 1f; // a disabled handler kills its coroutine — never leave the factor stuck high
    }

    // Called by ActionManager once before the first day of a new action.
    // Resets the duration so day 1 always starts at full 5 s.
    public void ResetForNewAction()
    {
        actionDayIndex = 0;
    }

    public void StartCycle(int cycles)
    {
        if (currentCycle != null)
            StopCoroutine(currentCycle);

        IsIdle = false;   // ← was missing; caused AdvanceTimeStepped to never wait
        currentCycle = StartCoroutine(RunCycles(cycles));
    }

    private IEnumerator RunCycles(int cycles)
    {
        for (int i = 0; i < cycles; i++)
        {
            float currentDuration = Mathf.Max(
                dayDuration / Mathf.Pow(2f, actionDayIndex),
                minDuration
            );

            // Publish the effective time-lapse factor (1..16) — atmosphere FX scroll faster with it.
            TimeFlowSignal.SpeedFactor = dayDuration / currentDuration;

            float elapsed = 0f;
            float rotationSpeed = 360f / currentDuration;

            while (elapsed < currentDuration)
            {
                float delta = Time.deltaTime;
                directionalLight.transform.Rotate(Vector3.right * (rotationSpeed * delta));
                elapsed += delta;
                yield return null;
            }

            // Snap back to exact start angle — kills any float drift from delta accumulation
            
            directionalLight.transform.rotation = _dayStartRotation;

            actionDayIndex++;
            OnCycleEnd?.Invoke(i + 1);
        }

        currentCycle = null;
        TimeFlowSignal.SpeedFactor = 1f; // time-lapse over — atmosphere FX ease back to real time
        IsIdle = true;
    }
}
