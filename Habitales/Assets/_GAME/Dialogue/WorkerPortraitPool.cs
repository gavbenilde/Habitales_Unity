using System.Collections.Generic;
using UnityEngine;

namespace Habitales
{
    [CreateAssetMenu(fileName = "WorkerPortraitPool", menuName = "Habitales/Workers/Worker Portrait Pool")]
    public class WorkerPortraitPool : ScriptableObject
    {
        [Header("Stock Photos")]
        public List<Sprite> stockPhotos = new List<Sprite>();

        // Runtime state — not serialized
        private HashSet<int> _usedIndices = new HashSet<int>();

        public Sprite GetNextPortrait()
        {
            if (stockPhotos == null || stockPhotos.Count == 0) return null;

            // All used — reset and allow repeats again
            if (_usedIndices.Count >= stockPhotos.Count)
                _usedIndices.Clear();

            // Build unused candidates
            var candidates = new List<int>();
            for (int i = 0; i < stockPhotos.Count; i++)
                if (!_usedIndices.Contains(i)) candidates.Add(i);

            int chosen = candidates[Random.Range(0, candidates.Count)];
            _usedIndices.Add(chosen);
            return stockPhotos[chosen];
        }
    }
}