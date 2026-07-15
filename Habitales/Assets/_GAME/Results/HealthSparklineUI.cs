using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Placeholder-grade world-health sparkline (arch ENDGAME_BUILD_PLAN §4). Renders
/// <see cref="SeasonReportData.healthHistory"/> as a polyline of pooled thin stretched
/// Images — no <c>UILineRenderer</c> dependency, no per-frame work. Y auto-scales to the
/// history's own min/max with a little padding; X is evenly spaced across the container.
/// Downsamples evenly when the history has more than <see cref="maxPoints"/> entries.
///
/// Pool is built once and reused across every <see cref="SetHistory"/> call — segments
/// beyond the count needed for the current history are simply deactivated, not destroyed.
///
/// Optional peak marker (WO-END-6): pass a <c>markerIndex</c> into the original,
/// pre-downsample history and a dot is placed at that point once downsampling has mapped
/// it onto the rendered polyline. Marker visuals (<see cref="markerPrefab"/>) are
/// optional-with-warning — the polyline still renders correctly without them.
///
/// Optional progressive reveal (Results Minipanel Carousel): <see cref="PlayProgressiveReveal"/>
/// sweeps the already-built polyline on screen left-to-right over time instead of the
/// instant reveal <see cref="SetHistory"/> itself produces — <c>EndGameScreenUI</c> calls it
/// every time the player lands on the sparkline slide of the results carousel. It replays
/// over whatever <see cref="SetHistory"/> last built; it does not rebuild the polyline.
///
/// Windowed mode (2026-07-15 check-in rework): <see cref="SetWindowed"/> renders the
/// check-in graph — the solid last <c>windowDays</c> of history on the LEFT half of the
/// rect ("today" at center) plus a dashed projection extending <c>windowDays</c> ahead on
/// the RIGHT half, at the caller-supplied slope, clamped to health 0–100 and to the run's
/// final day. Y is FIXED 0–100 in this mode (the boundary/axis art is static prefab
/// Images); auto-scaling stays exclusive to <see cref="SetHistory"/>. Dashes reuse the
/// same segment pool, one dash cell per projected day (<see cref="dashFillRatio"/> filled).
/// Date labels under the boundary lines are NOT this class's job — the caller
/// (CheckInPanelUI) fills them; this stays a pure renderer.
///
/// <see cref="animateReveal"/> (default FALSE) gates the reveal of BOTH build APIs:
/// false = complete in one frame (the check-in's instant "generated report" look, and
/// SetHistory's unchanged legacy behavior); true = a left-to-right per-segment sweep on
/// unscaled time after each build.
///
/// WIRING (human):
///   1. Add this component to a RectTransform sized to the sparkline's on-screen area —
///      that RectTransform IS the plot area (segments are placed in its local space).
///   2. Assign <c>segmentPrefab</c> — a thin Image (e.g. a 1x1 white sprite stretched by
///      this script; a plain UI Image with no sprite works too, it'll just be a flat color
///      rect). Prefab does not need to live in the scene; it is instantiated under this
///      RectTransform and immediately deactivated as the pool seed.
///   3. Assign <c>tooEarlyLabel</c> (optional) — a GameObject (e.g. TMP text reading
///      "Too early to chart") shown instead of the polyline when history.Count &lt; 2.
///   4. Assign <c>markerPrefab</c> (optional) — a small square/round Image used as the
///      peak-day dot. Not required for callers that never pass a markerIndex and use the
///      single-arg SetHistory overload. (Live consumers: EndGameScreenUI.seasonSparkline
///      via SetHistory, CheckInPanelUI.graph via SetWindowed.)
///   5. For a check-in (windowed) instance: the two boundary marker lines (solid black
///      left edge, dashed black right edge) are STATIC prefab Images placed at the rect
///      edges by hand — this script never moves them. Tune <c>dashFillRatio</c> /
///      <c>projectionColor</c> for the projection's look.
/// </summary>
public class HealthSparklineUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform plotArea;      // defaults to this GameObject's RectTransform
    [SerializeField] private Image         segmentPrefab; // pool seed — a thin stretched Image
    [SerializeField] private GameObject    tooEarlyLabel;  // optional — shown when history.Count < 2

    [Header("Style")]
    [SerializeField] private float lineThickness = 3f;
    [SerializeField] private Color lineColor = Color.white;
    [SerializeField] private float yPaddingFraction = 0.1f; // headroom above/below min/max

    [Header("Downsampling")]
    [SerializeField] private int maxPoints = 60; // history longer than this is evenly downsampled

    [Header("Windowed Mode (check-in graph — SetWindowed only)")]
    [Tooltip("Color of the dashed projection line. The solid history half keeps Line Color.")]
    [SerializeField] private Color projectionColor = new Color(1f, 1f, 1f, 0.55f);
    [Range(0.1f, 0.9f)]
    [Tooltip("Filled fraction of each dash cell on the projection (one cell per projected day). 0.6 = dash 60%, gap 40%.")]
    [SerializeField] private float dashFillRatio = 0.6f;

    [Header("Reveal (gates BOTH SetHistory and SetWindowed)")]
    [Tooltip("FALSE (default) = builds complete in one frame — the check-in's instant 'generated report' look and SetHistory's unchanged legacy behavior. TRUE = left-to-right per-segment sweep on unscaled time after each build.")]
    [SerializeField] private bool animateReveal = false;
    [Tooltip("Sweep duration when Animate Reveal is on.")]
    [SerializeField] private float revealSeconds = 0.75f;

    [Header("Peak Marker (OPTIONAL — polyline still renders unwired, see SetHistory())")]
    [Tooltip("Dot placed at the marker index's point. Leave unassigned to skip the marker entirely.")]
    [SerializeField] private Image markerPrefab;
    [SerializeField] private Color markerColor = Color.yellow;
    [SerializeField] private Vector2 markerSize = new Vector2(10f, 10f);

    private readonly List<Image> _pool = new List<Image>();
    private Image _markerInstance;
    private bool  _markerWarned;

    // Last-rendered state, kept so PlayProgressiveReveal can sweep over exactly what
    // SetHistory built (segment count + which rendered point carries the peak marker).
    private int _renderedSegmentCount;
    private int _renderedMarkerPoint = -1; // rendered-point index, -1 = no marker
    private int _revealTweenId = -1;

    private void Awake()
    {
        if (plotArea == null) plotArea = transform as RectTransform;

        if (segmentPrefab == null)
        {
            Debug.LogError($"{name}: HealthSparklineUI.segmentPrefab missing — wire a thin Image in the Inspector.", this);
            enabled = false;
            return;
        }

        segmentPrefab.gameObject.SetActive(false);

        if (markerPrefab != null)
        {
            markerPrefab.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Rebuilds the polyline from <paramref name="history"/>. Rebuild-only-in-Show() —
    /// callers should not invoke this per-frame. Guards history.Count &lt; 2 by hiding the
    /// polyline and showing <see cref="tooEarlyLabel"/> instead.
    ///
    /// <paramref name="markerIndex"/> is an index into the ORIGINAL (pre-downsample)
    /// <paramref name="history"/> — e.g. EndGameData.peakAtDay. Pass -1 (default) to skip
    /// the marker. Out-of-range indices are ignored with a hidden marker, not an error —
    /// a missing/invalid peak day should never block the sparkline itself.
    /// </summary>
    public void SetHistory(IReadOnlyList<float> history, int markerIndex = -1)
    {
        if (!enabled) return;

        // A rebuild invalidates whatever an in-flight progressive-reveal sweep was
        // animating over — cancel it first so it can't reactivate stale pool entries
        // mid-rebuild (segments below are about to be repositioned/deactivated).
        CancelProgressiveReveal();

        if (history == null || history.Count < 2)
        {
            SetAllSegmentsActive(false);
            SetMarkerActive(false);
            if (tooEarlyLabel != null) tooEarlyLabel.SetActive(true);
            _renderedSegmentCount = 0;
            _renderedMarkerPoint = -1;
            return;
        }

        if (tooEarlyLabel != null) tooEarlyLabel.SetActive(false);

        int originalCount = history.Count;
        IReadOnlyList<float> points = Downsample(history, maxPoints);

        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] < min) min = points[i];
            if (points[i] > max) max = points[i];
        }

        float range = max - min;
        if (range < 0.01f) range = 0.01f; // flat history — avoid div-by-zero, draw a flat line
        float pad = range * yPaddingFraction;
        min -= pad;
        max += pad;
        range = max - min;

        float width  = plotArea.rect.width;
        float height = plotArea.rect.height;

        int segmentCount = points.Count - 1;
        EnsurePoolSize(segmentCount);

        for (int i = 0; i < segmentCount; i++)
        {
            Vector2 a = PointPosition(points[i],     i,     points.Count, width, height, min, range);
            Vector2 b = PointPosition(points[i + 1], i + 1, points.Count, width, height, min, range);
            PlaceSegment(_pool[i], a, b);
        }

        for (int i = segmentCount; i < _pool.Count; i++)
            _pool[i].gameObject.SetActive(false);

        _renderedSegmentCount = segmentCount;

        PlaceMarker(points, originalCount, markerIndex, width, height, min, range);

        if (animateReveal) PlayProgressiveReveal(revealSeconds);
    }

    /// <summary>
    /// Windowed check-in build (2026-07-15 rework): solid polyline of the last
    /// min(<paramref name="windowDays"/>, history.Count) samples on the LEFT half of the
    /// rect (the newest sample lands exactly at center = "today"), plus a dashed
    /// projection from that last point at <paramref name="slopePerDay"/> per day for up
    /// to <paramref name="windowDays"/> days — clamped to health 0–100 and to the run's
    /// remaining days (<paramref name="runLengthDays"/> − <paramref name="totalDays"/>).
    /// Y is FIXED 0–100 (static prefab axis art). Rebuild-only-in-Show(), like SetHistory.
    /// The peak marker is a SetHistory-only feature — always hidden here.
    /// </summary>
    public void SetWindowed(IReadOnlyList<float> history, int windowDays, float slopePerDay, int totalDays, int runLengthDays)
    {
        if (!enabled) return;

        CancelProgressiveReveal();

        if (history == null || history.Count < 2 || windowDays < 1)
        {
            SetAllSegmentsActive(false);
            SetMarkerActive(false);
            if (tooEarlyLabel != null) tooEarlyLabel.SetActive(true);
            _renderedSegmentCount = 0;
            _renderedMarkerPoint = -1;
            return;
        }

        if (tooEarlyLabel != null) tooEarlyLabel.SetActive(false);

        float width     = plotArea.rect.width;
        float height    = plotArea.rect.height;
        float halfWidth = width * 0.5f;

        // ── Solid half: newest sample at center, one sample per day back ──
        // windowDays + 1 samples span exactly [today − windowDays, today], so a full
        // window's oldest point lands ON the left boundary line (mirroring the dashed
        // side, whose projDays + 1 points reach the right boundary). A short history
        // simply starts partway into the left half instead of stretching to fill it.
        int solidCount = Mathf.Min(windowDays + 1, history.Count);
        var solidPoints = BuildWindowedPoints(
            sampleCount: solidCount,
            valueAt: i => history[history.Count - solidCount + i],
            xAt: i => halfWidth * (1f - (solidCount - 1 - i) / (float)windowDays),
            height: height);

        int solidSegments = solidPoints.Count - 1;
        EnsurePoolSize(solidSegments);
        for (int i = 0; i < solidSegments; i++)
            PlaceSegment(_pool[i], solidPoints[i], solidPoints[i + 1], lineColor);

        int used = solidSegments;

        // ── Dashed half: projection from the last solid point ──
        float lastValue = history[history.Count - 1];
        int projDays = Mathf.Min(windowDays, Mathf.Max(0, runLengthDays - totalDays));
        if (projDays >= 1)
        {
            var projPoints = BuildWindowedPoints(
                sampleCount: projDays + 1, // day 0 (today) .. day projDays
                valueAt: i => lastValue + slopePerDay * i,
                xAt: i => halfWidth + halfWidth * (i / (float)windowDays),
                height: height);

            // One dash cell per consecutive point pair: fill only dashFillRatio of each
            // cell so the projection reads as dashed with zero extra pool machinery.
            int dashSegments = projPoints.Count - 1;
            EnsurePoolSize(used + dashSegments);
            for (int i = 0; i < dashSegments; i++)
            {
                Vector2 a = projPoints[i];
                Vector2 b = a + (projPoints[i + 1] - a) * dashFillRatio;
                PlaceSegment(_pool[used + i], a, b, projectionColor);
            }
            used += dashSegments;
        }

        for (int i = used; i < _pool.Count; i++)
            _pool[i].gameObject.SetActive(false);

        _renderedSegmentCount = used;
        _renderedMarkerPoint = -1;
        SetMarkerActive(false);

        if (animateReveal) PlayProgressiveReveal(revealSeconds);
    }

    /// <summary>
    /// Maps <paramref name="sampleCount"/> samples onto plot-space points with a fixed
    /// 0–100 y-scale, downsampling to <see cref="maxPoints"/> by original-index remap so
    /// day→x positions stay honest after thinning.
    /// </summary>
    private List<Vector2> BuildWindowedPoints(int sampleCount, System.Func<int, float> valueAt,
                                              System.Func<int, float> xAt, float height)
    {
        int rendered = Mathf.Min(sampleCount, Mathf.Max(2, maxPoints));
        var points = new List<Vector2>(rendered);
        for (int i = 0; i < rendered; i++)
        {
            int src = rendered == sampleCount
                ? i
                : Mathf.RoundToInt(i * (sampleCount - 1) / (float)(rendered - 1));
            float y = Mathf.Clamp(valueAt(src), 0f, 100f) / 100f * height;
            points.Add(new Vector2(xAt(src), y));
        }
        return points;
    }

    private static Vector2 PointPosition(float value, int index, int count, float width, float height, float min, float range)
    {
        float x = count <= 1 ? 0f : (index / (float)(count - 1)) * width;
        float t = (value - min) / range;
        float y = t * height;
        return new Vector2(x, y);
    }

    private void PlaceSegment(Image segment, Vector2 a, Vector2 b)
    {
        PlaceSegment(segment, a, b, lineColor);
    }

    private void PlaceSegment(Image segment, Vector2 a, Vector2 b, Color color)
    {
        var rt = segment.rectTransform;
        segment.gameObject.SetActive(true);
        segment.color = color;

        Vector2 delta = b - a;
        float length = delta.magnitude;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.anchoredPosition = a;
        rt.sizeDelta = new Vector2(Mathf.Max(length, 0.01f), lineThickness);
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void EnsurePoolSize(int needed)
    {
        while (_pool.Count < needed)
        {
            var seg = Instantiate(segmentPrefab, plotArea);
            _pool.Add(seg);
        }
    }

    private void SetAllSegmentsActive(bool active)
    {
        for (int i = 0; i < _pool.Count; i++)
            _pool[i].gameObject.SetActive(active);
    }

    private void SetMarkerActive(bool active)
    {
        if (_markerInstance != null)
            _markerInstance.gameObject.SetActive(active);
    }

    /// <summary>
    /// Places the optional peak-day dot on the rendered polyline. <paramref name="markerIndex"/>
    /// is an index into the ORIGINAL history; it is remapped through the same even spacing as
    /// <see cref="Downsample"/> so the dot lands on an actual rendered point (not between two).
    /// Out-of-range/-1 indices hide the marker; a wired-but-unneeded prefab costs nothing.
    /// </summary>
    private void PlaceMarker(IReadOnlyList<float> points, int originalCount, int markerIndex,
                             float width, float height, float min, float range)
    {
        if (markerIndex < 0 || markerIndex >= originalCount)
        {
            SetMarkerActive(false);
            _renderedMarkerPoint = -1;
            return;
        }

        if (markerPrefab == null)
        {
            if (!_markerWarned)
            {
                Debug.LogWarning($"{name}: HealthSparklineUI — a markerIndex was passed but markerPrefab is not wired; the peak dot is skipped. Assign a small Image to show it.", this);
                _markerWarned = true;
            }
            _renderedMarkerPoint = -1;
            return;
        }

        if (_markerInstance == null)
            _markerInstance = Instantiate(markerPrefab, plotArea);

        // Remap the original index onto the (possibly downsampled) rendered points.
        int rendered = points.Count == originalCount
            ? markerIndex
            : Mathf.RoundToInt(markerIndex * (points.Count - 1) / (float)(originalCount - 1));
        rendered = Mathf.Clamp(rendered, 0, points.Count - 1);
        _renderedMarkerPoint = rendered;

        Vector2 p = PointPosition(points[rendered], rendered, points.Count, width, height, min, range);

        var rt = _markerInstance.rectTransform;
        _markerInstance.gameObject.SetActive(true);
        _markerInstance.color = markerColor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.sizeDelta = markerSize;
        rt.anchoredPosition = p;
        rt.SetAsLastSibling(); // draw the dot above the line segments
    }

    /// <summary>
    /// Sweeps the polyline built by the last <see cref="SetHistory"/> call on screen
    /// left-to-right over <paramref name="seconds"/> — the run's health history "draws
    /// itself". Does not rebuild anything; it only replays visibility over the segments
    /// already placed. Safe to call repeatedly (e.g. every time the caller's carousel
    /// lands back on the sparkline slide) — each call cancels any sweep already in flight.
    /// </summary>
    public void PlayProgressiveReveal(float seconds)
    {
        if (!enabled) return;

        CancelProgressiveReveal();

        if (_renderedSegmentCount < 1) return;

        if (seconds <= 0f)
        {
            RevealAll();
            return;
        }

        int total = _renderedSegmentCount;

        for (int i = 0; i < total; i++)
            _pool[i].gameObject.SetActive(false);
        SetMarkerActive(false);

        LTDescr tween = LeanTween.value(gameObject, 0f, total, seconds)
            .setIgnoreTimeScale(true)
            .setOnUpdate((float v) =>
            {
                int visible = Mathf.Clamp(Mathf.CeilToInt(v), 0, total);
                for (int i = 0; i < visible; i++)
                {
                    if (!_pool[i].gameObject.activeSelf)
                        _pool[i].gameObject.SetActive(true);
                }
                if (_renderedMarkerPoint >= 0 && visible >= _renderedMarkerPoint)
                    SetMarkerActive(true);
            })
            .setOnComplete(() =>
            {
                _revealTweenId = -1;
                RevealAll();
            });

        _revealTweenId = tween.uniqueId;
    }

    /// <summary>
    /// Cancels an in-flight <see cref="PlayProgressiveReveal"/> sweep, landing on the
    /// FULL polyline — never a partial one. Safe to call with no sweep running.
    /// </summary>
    public void CancelProgressiveReveal()
    {
        if (_revealTweenId == -1) return;
        LeanTween.cancel(_revealTweenId);
        _revealTweenId = -1;
        RevealAll();
    }

    /// <summary>
    /// Idempotent final state: every segment from the last <see cref="SetHistory"/>
    /// build visible, marker shown iff one was placed.
    /// </summary>
    private void RevealAll()
    {
        int count = Mathf.Min(_renderedSegmentCount, _pool.Count);
        for (int i = 0; i < count; i++)
            _pool[i].gameObject.SetActive(true);
        SetMarkerActive(_renderedMarkerPoint >= 0);
    }

    private static IReadOnlyList<float> Downsample(IReadOnlyList<float> source, int maxCount)
    {
        if (source.Count <= maxCount) return source;

        var result = new List<float>(maxCount);
        for (int i = 0; i < maxCount; i++)
        {
            int srcIndex = Mathf.RoundToInt(i * (source.Count - 1) / (float)(maxCount - 1));
            result.Add(source[srcIndex]);
        }
        return result;
    }
}
