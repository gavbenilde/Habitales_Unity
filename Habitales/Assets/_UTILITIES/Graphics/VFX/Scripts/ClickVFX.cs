using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Pooled one-shot emitter for the selection "poof" (2026-07-29 juice pass).
///
/// <para>WAS: an independent Update that raycast on every LMB-down and did Instantiate + Destroy
/// per poof. Two problems. It fired on ANY tile click — including clicks the selection REFUSED
/// (over budget, or a tile a filtered cleanup action can't act on) — so the game cheered for a
/// no-op. And it ran a second raycast (all layers, matched by the "Tile" tag) alongside the one
/// TileSelector already does, which could disagree about what was clicked.</para>
///
/// <para>NOW: a passive pool that TileSelector drives, so a poof appears exactly when the
/// selection actually did something. The pooling is not premature — the drag stroke emits
/// continuously, and the old Instantiate/Destroy pair churned GameObjects *and* VFX Graph
/// component instances at ~15/sec, which is precisely the fast-swipe case the effect exists to
/// reward.</para>
///
/// <para>Scene wiring: the <c>vfx</c> field keeps its name and type, so the existing ClickPoof
/// prefab reference on the scene object survives untouched. The old <c>cam</c> field is gone —
/// TileSelector owns the raycast now.</para>
/// </summary>
public class ClickVFX : MonoBehaviour
{
    [Header("Poof")]
    [Tooltip("The poof prefab (ClickPoof). Its VisualEffect is re-triggered per emit, never re-instantiated.")]
    [SerializeField] private GameObject vfx;

    [Tooltip("How many poofs may overlap before the oldest is recycled mid-flight. A hard swipe " +
             "emits roughly one per tile crossed plus the drag texture, so ~16 covers it.")]
    [SerializeField] private int poolSize = 16;

    /// <summary>Law-1 read-only singleton. Set in Awake; Law-3 warning on double-instance.</summary>
    public static ClickVFX Instance { get; private set; }

    private readonly List<Transform>    _xform = new List<Transform>();
    private readonly List<VisualEffect> _fx    = new List<VisualEffect>();
    private Transform _poolRoot;
    private int _next;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[ClickVFX] Duplicate emitter on '{name}' — destroying the component. " +
                             "Only one ClickVFX should exist per scene.", this);
            Destroy(this);
            return;
        }
        Instance = this;

        if (vfx == null)
        {
            Debug.LogError($"{name}: ClickVFX has no poof prefab assigned — selection VFX is disabled.", this);
            enabled = false;
            return;
        }

        Prewarm();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_poolRoot != null) Destroy(_poolRoot.gameObject);
    }

    /// <summary>
    /// Pre-instantiates the whole pool under a scene-root container. Deliberately NOT parented to
    /// this component's transform — that is the camera rig, and a parented poof would slide along
    /// with the pan instead of staying on the tile it marked.
    /// </summary>
    void Prewarm()
    {
        _poolRoot = new GameObject("ClickPoofPool").transform;

        for (int i = 0; i < Mathf.Max(1, poolSize); i++)
        {
            GameObject go = Instantiate(vfx, _poolRoot);
            go.SetActive(false);
            _xform.Add(go.transform);
            _fx.Add(go.GetComponent<VisualEffect>());
        }
    }

    /// <summary>
    /// Fires one poof at <paramref name="worldPos"/>.
    ///
    /// <para><paramref name="scale"/> is the whole point of the two-layer model: the drag TEXTURE
    /// emits small and often, the tile-commit BEAT emits full size. Same prefab, different read.</para>
    ///
    /// <para>Recycles round-robin. A poof still playing when its slot comes back around is simply
    /// restarted — correct at this emission density (nobody can pick out the one that got cut) and
    /// it hard-caps the cost no matter how fast the player swipes.</para>
    /// </summary>
    public void Emit(Vector3 worldPos, float scale = 1f)
    {
        if (_xform.Count == 0) return;

        int i = _next;
        _next = (_next + 1) % _xform.Count;

        Transform t = _xform[i];
        t.position   = worldPos;
        t.rotation   = vfx.transform.rotation;   // the prefab's authored flat-to-ground rotation
        t.localScale = Vector3.one * Mathf.Max(0.01f, scale);

        // Left active once woken: a finished VFX Graph with no live particles is cheap, and
        // toggling SetActive every emit would just churn OnEnable/OnDisable.
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);

        VisualEffect fx = _fx[i];
        if (fx != null)
        {
            fx.Reinit();   // rewind; the prefab has ResetSeedOnPlay, so each poof varies
            fx.Play();
        }
    }
}
