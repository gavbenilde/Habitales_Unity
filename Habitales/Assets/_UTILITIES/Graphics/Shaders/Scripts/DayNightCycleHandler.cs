using System.Collections;
using UnityEngine;

public class DayNightCycleHandler : MonoBehaviour
{
    [SerializeField] private GameObject directionalLight;

    public float dayDuration = 5f;

    private Coroutine currentCycle;

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
        float rotationSpeed = 360f / dayDuration;

        for (int i = 0; i < cycles; i++)
        {
            float elapsed = 0f;

            while (elapsed < dayDuration)
            {
                float delta = Time.deltaTime;
                float rotationThisFrame = rotationSpeed * delta;

                directionalLight.transform.Rotate(Vector3.right * rotationThisFrame);

                elapsed += delta;
                yield return null;
            }
        }

        currentCycle = null;
    }
}