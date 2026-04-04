using System;
using System.Collections;
using UnityEngine;

namespace UTILITIES.Camera
{
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class EventCameraHandler : MonoBehaviour
    {
        public static EventCameraHandler Instance { get; private set; }

        [Header("Pan Settings")]
        [SerializeField] private float panDuration = 0.6f;
        [SerializeField] private AnimationCurve panCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public bool IsPanning { get; private set; } = false;

        private Vector3 _originPosition;
        private Coroutine _activeCoroutine;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        // Called by EventManager before showing a popup.
        // Stores the return origin, pans to target, then fires onComplete.
        public void PanTo(Vector3 worldTarget, Action onComplete = null)
        {
            _originPosition = transform.position;
            // Only move XZ — Y is locked to preserve camera angle
            Vector3 destination = new Vector3(worldTarget.x, transform.position.y, worldTarget.z);

            if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
            _activeCoroutine = StartCoroutine(PanCoroutine(destination, onComplete));
        }
        
        public void PanTo(Vector3 worldTarget)
        {
            _originPosition = transform.position;
            // Only move XZ — Y is locked to preserve camera angle
            Vector3 destination = new Vector3(worldTarget.x, transform.position.y, worldTarget.z);
        }
        

        // Called by EventManager when the event queue empties.
        // Pans back to stored origin, then fires onComplete (which resumes the game).
        public void ReturnToOrigin(Action onComplete = null)
        {
            if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
            _activeCoroutine = StartCoroutine(PanCoroutine(_originPosition, () =>
            {
                IsPanning = false;
                onComplete?.Invoke();
            }));
        }

        private IEnumerator PanCoroutine(Vector3 destination, Action onComplete)
        {
            IsPanning = true;
            Vector3 start = transform.position;
            float elapsed = 0f;

            while (elapsed < panDuration)
            {
                elapsed += Time.deltaTime;
                float t = panCurve.Evaluate(Mathf.Clamp01(elapsed / panDuration));
                transform.position = Vector3.Lerp(start, destination, t);
                yield return null;
            }

            transform.position = destination;
            onComplete?.Invoke();
        }
    }
}