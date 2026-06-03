using System;
using System.Collections;
using _UTILITIES.Camera;
using UnityEngine;

namespace UTILITIES.Camera
{
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class EventCameraHandler : MonoBehaviour
    {
        public static EventCameraHandler Instance { get; private set; }

        private UnityEngine.Camera _camera;
        private CameraZoomOrtho _zoomController;
        
        [Header("Pan Settings")]
        [SerializeField] private float panDuration = 0.6f;
        [SerializeField] private AnimationCurve panCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        
        [Header("Zoom Settings")]
        [SerializeField] private float zoomDuration = 0.6f;
        [SerializeField] private AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public bool IsPanning { get; private set; } = false;
        public bool IsZooming { get; private set; } = false;

        private Vector3 _originPosition;
        private Coroutine _panCoroutine;
        private Coroutine _zoomCoroutine;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            
            _camera = GetComponent<UnityEngine.Camera>();
            _zoomController = GetComponent<CameraZoomOrtho>();
        }

        // Called by EventManager before showing a popup.
        // Stores the return origin, pans to target, then fires onComplete.
        public void PanTo(Vector3 worldTarget, Action onComplete = null)
        {
            _originPosition = transform.position;
            // Only move XZ — Y is locked to preserve camera angle
            Vector3 destination = new Vector3(worldTarget.x, transform.position.y, worldTarget.z);

            if (_panCoroutine != null) StopCoroutine(_panCoroutine);
            _panCoroutine = StartCoroutine(PanCoroutine(destination, onComplete));
        }
        
        public void PanTo(Vector3 worldTarget)
        {
            _originPosition = transform.position;
            // Only move XZ — Y is locked to preserve camera angle
            Vector3 destination = new Vector3(worldTarget.x, transform.position.y, worldTarget.z);
            
            if (_panCoroutine != null) StopCoroutine(_panCoroutine);
            _panCoroutine = StartCoroutine(PanCoroutine(destination));
        }
        
        public void ZoomTo(float targetZoom, Action onComplete = null)
        {
            if (_zoomController != null)
            {
                _zoomController.SetZoom(targetZoom);
            }
            
            if (_zoomCoroutine != null) StopCoroutine(_zoomCoroutine);
            _zoomCoroutine = StartCoroutine(ZoomCoroutine(targetZoom, onComplete));
        }
        
        public void ZoomTo(float targetZoom)
        {
            if (_zoomController != null)
            {
                _zoomController.SetZoom(targetZoom);
            }
            
            if (_zoomCoroutine != null) StopCoroutine(_zoomCoroutine);
            _zoomCoroutine = StartCoroutine(ZoomCoroutine(targetZoom));
        }
        

        // Called by EventManager when the event queue empties.
        // Pans back to stored origin, then fires onComplete (which resumes the game).
        public void ReturnToOrigin(Action onComplete = null)
        {
            if (_panCoroutine != null) StopCoroutine(_panCoroutine);
            _panCoroutine = StartCoroutine(PanCoroutine(_originPosition, () =>
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
        
        private IEnumerator PanCoroutine(Vector3 destination)
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
            IsPanning = false;
        }

        private IEnumerator ZoomCoroutine(float targetZoom, Action onComplete)
        {
            IsZooming = true;

            float startSize = _camera.orthographicSize;
            float elapsed = 0f;

            while (elapsed < zoomDuration)
            {
                elapsed += Time.deltaTime;
                float t = zoomCurve.Evaluate(Mathf.Clamp01(elapsed / zoomDuration));

                _camera.orthographicSize = Mathf.Lerp(startSize, targetZoom, t);

                yield return null;
            }

            _camera.orthographicSize = targetZoom;

            IsZooming = false;
            onComplete?.Invoke();
        }
        
        private IEnumerator ZoomCoroutine(float targetZoom)
        {
            IsZooming = true;

            float startSize = _camera.orthographicSize;
            float elapsed = 0f;

            while (elapsed < zoomDuration)
            {
                elapsed += Time.deltaTime;
                float t = zoomCurve.Evaluate(Mathf.Clamp01(elapsed / zoomDuration));

                _camera.orthographicSize = Mathf.Lerp(startSize, targetZoom, t);

                yield return null;
            }

            _camera.orthographicSize = targetZoom;

            IsZooming = false;
        }
    }
}