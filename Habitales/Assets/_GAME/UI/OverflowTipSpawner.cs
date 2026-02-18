using UnityEngine;

public class OverflowTipSpawner : MonoBehaviour
{
    public static OverflowTipSpawner Instance { get; private set; }

    [Header("References")]
    [SerializeField] private GameObject tipPrefab;
    [SerializeField] private Canvas tipCanvas;

    // Track the single live tip
    private GameObject activeTip;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// Spawns a tip at the cursor. Destroys any existing tip immediately first.
    /// </summary>
    public void SpawnAtCursor(string message)
    {
        if (tipPrefab == null || tipCanvas == null)
        {
            Debug.LogWarning("OverflowTipSpawner: tipPrefab or tipCanvas not assigned!");
            return;
        }

        // Kill the previous tip instantly — no lingering clutter
        if (activeTip != null)
            Destroy(activeTip);

        activeTip = Instantiate(tipPrefab, tipCanvas.transform);
        activeTip.transform.position = Input.mousePosition + new Vector3(0f, 20f, 0f);

        OverflowTip overflowTip = activeTip.GetComponent<OverflowTip>();
        if (overflowTip != null)
            overflowTip.Initialize(message);
        else
            Debug.LogWarning("OverflowTipSpawner: tipPrefab is missing OverflowTip component!");
    }
}