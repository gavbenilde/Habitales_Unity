using System.Collections.Generic;

namespace Habitales.Core
{
    /// <summary>
    /// Reusable rolling trend window (extracted 2026-07-22). Holds the last
    /// <see cref="RunManager.TrendWindowDays"/> + 1 daily samples and exposes the smoothed
    /// per-day change across them — exactly the inline math RegionManager.HandleDayResolved
    /// and RunManager.WorldHealthTrend already do (averaging daily deltas telescopes to
    /// (newest − oldest) / span, so the queue is all we need).
    ///
    /// Owners <see cref="Enqueue"/> one sample per resolved day and read <see cref="Trend"/>
    /// (Law 1 — passive signal, not a run-progress record; keep those separate).
    /// </summary>
    public class TrendWindow
    {
        private readonly Queue<float> _samples = new Queue<float>();
        private float _newest;

        /// <summary>Pushes one daily sample, rolling the oldest off once the window is full.</summary>
        public void Enqueue(float sample)
        {
            _newest = sample;
            _samples.Enqueue(sample);

            // Window + 1 samples span exactly TrendWindowDays daily deltas.
            while (_samples.Count > RunManager.TrendWindowDays + 1)
                _samples.Dequeue();
        }

        /// <summary>
        /// Average daily change over the window == (newest − oldest) / span, in the sample's
        /// own units (health-points here). 0 until at least two samples exist.
        /// </summary>
        public float Trend
        {
            get
            {
                int span = _samples.Count - 1;
                return span > 0 ? (_newest - _samples.Peek()) / span : 0f;
            }
        }

        /// <summary>Number of samples currently held (≤ TrendWindowDays + 1).</summary>
        public int SampleCount => _samples.Count;

        /// <summary>Drops every sample — the next Enqueue starts a fresh window.</summary>
        public void Clear()
        {
            _samples.Clear();
            _newest = 0f;
        }
    }
}
