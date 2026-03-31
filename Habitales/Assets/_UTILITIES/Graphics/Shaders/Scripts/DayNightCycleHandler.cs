using System;
using System.Collections;
using UnityEngine;

public class DayNightCycleHandler : MonoBehaviour
{
    [SerializeField] private GameObject directionalLight;
    private readonly float dayDuration = 5f;
    
    private Coroutine currentCycle;

    public event Action<int> OnCycleEnd;

    void OnEnable()
    {
        if (ResourceManager.Instance != null)
            ResourceManager.Instance.OnTimeAdvanced += StartCycle;
    }

    void OnDisable()
    {
        if (ResourceManager.Instance != null)
            ResourceManager.Instance.OnTimeAdvanced -= StartCycle;
    }

    public void StartCycle(int cycles)
    {
        if (currentCycle != null)
            StopCoroutine(currentCycle);

        currentCycle = StartCoroutine(RunCycles(cycles));
    }

    private IEnumerator RunCycles(int cycles)
    {
        float baseDuration = dayDuration;
        
        float minDuration = 0.3125f;

        float currentDuration = baseDuration;

        for (int i = 0; i < cycles; i++)
        {
            float elapsed = 0f;
            float rotationSpeed = 360f / currentDuration;

            while (elapsed < currentDuration)
            {
                float delta = Time.deltaTime;
                float rotationThisFrame = rotationSpeed * delta;

                directionalLight.transform.Rotate(Vector3.right * rotationThisFrame);

                elapsed += delta;
                yield return null;
            }
            
            OnCycleEnd?.Invoke(i + 1);
            
            currentDuration *= 0.5f;
            
            currentDuration = Mathf.Max(currentDuration, minDuration);
        }

        currentCycle = null;
    }
}