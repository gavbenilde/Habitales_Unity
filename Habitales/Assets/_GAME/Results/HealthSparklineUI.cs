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
///      single-arg SetHistory overload. (Sole live consumer is EndGameScreenUI — the
///      mid-run check-in panel deliberately renders no sparkline.)
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

    [Header("Peak Marker (OPTIONAL — polyline still renders unwired, see SetHistory())")]
    [Tooltip("Dot placed at the marker index's point. Leave unassigned to skip the marker entirely.")]
    [SerializeField] private Image markerPrefab;
    [SerializeField] private Color markerColor = Color.yellow;
    [SerializeField] private Vector2 markerSize = new Vector2(10f, 10f);

    private readonly List<Image> _pool = new List<Image>();
    private Image _markerInstance;
    private bool  _markerWarned;

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

        if (history == null || history.Count < 2)
        {
            SetAllSegmentsActive(false);
            SetMarkerActive(false);
            if (tooEarlyLabel != null) tooEarlyLabel.SetActive(true);
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

        PlaceMarker(points, originalCount, markerIndex, width, height, min, range);
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
        var rt = segment.rectTransform;
        segment.gameObject.SetActive(true);
        segment.color = lineColor;

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
            return;
        }

        if (markerPrefab == null)
        {
            if (!_markerWarned)
            {
                Debug.LogWarning($"{name}: HealthSparklineUI — a markerIndex was passed but markerPrefab is not wired; the peak dot is skipped. Assign a small Image to show it.", this);
                _markerWarned = true;
            }
            return;
        }

        if (_markerInstance == null)
            _markerInstance = Instantiate(markerPrefab, plotArea);

        // Remap the original index onto the (possibly downsampled) rendered points.
        int rendered = points.Count == originalCount
            ? markerIndex
            : Mathf.RoundToInt(markerIndex * (points.Count - 1) / (float)(originalCount - 1));
        rendered = Mathf.Clamp(rendered, 0, points.Count - 1);

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
