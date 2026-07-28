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

        [Tooltip("Where on screen the pan target lands, as a fraction of the screen from the centre. " +
                 "(0,0) = dead centre. Nudge y up (e.g. 0.12) when a popup covers the lower screen.")]
        [SerializeField] private Vector2 framingScreenOffset = Vector2.zero;

        [Tooltip("World Y of the ground plane the framing ray is measured against. Only used as a " +
                 "fallback — PanTo measures against a plane through the target itself.")]
        [SerializeField] private float groundPlaneY = 0f;

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

            if (_panCoroutine != null) StopCoroutine(_panCoroutine);
            _panCoroutine = StartCoroutine(PanCoroutine(DestinationFor(worldTarget), onComplete));
        }

        public void PanTo(Vector3 worldTarget)
        {
            _originPosition = transform.position;

            if (_panCoroutine != null) StopCoroutine(_panCoroutine);
            _panCoroutine = StartCoroutine(PanCoroutine(DestinationFor(worldTarget)));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Framing maths (fixed 2026-07-28 — "camera shifts too far" on the
        // phase-16 region-unlock pan).
        //
        // The old destination was `new Vector3(target.x, y, target.z)`: it put the
        // CAMERA'S OWN position over the target. On a tilted ortho rig that is not
        // where the camera LOOKS. This scene's camera sits at (20,23,20) with a
        // ~41° pitch, so the ground point at screen centre is roughly (0.1, 0, 3.1)
        // — the look-at point trails the camera position by ~(19.9, 16.9) world
        // units. With orthographicSize 7.5 that is several screens of overshoot,
        // which is exactly the reported symptom.
        //
        // The fix is the "blind man's stick": find the ground point the camera is
        // ALREADY looking at, and translate the camera by the delta from that point
        // to the target. Whatever the pitch, yaw, height or zoom, the target lands
        // under the framing reticle. We intersect a maths plane rather than
        // Physics.Raycast on purpose: no collider dependency, works before tiles
        // have spawned visuals, and it cannot miss — a real raycast returns nothing
        // precisely when the camera is looking past the edge of the grid, which is
        // the case that needs framing most.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Camera position that puts <paramref name="worldTarget"/> under the framing point.
        /// Y is preserved (the ground delta is flat by construction). Falls back to the old
        /// position-over-target behaviour only if the framing ray can't reach the ground.
        /// </summary>
        private Vector3 DestinationFor(Vector3 worldTarget)
        {
            if (TryGetGroundFocus(worldTarget.y, out Vector3 focus))
                return transform.position + (worldTarget - focus);

            Debug.LogWarning($"{name}: framing ray never meets the ground plane (camera not looking " +
                             "down?) — falling back to moving the camera onto the target, which " +
                             "overshoots on a tilted rig.", this);
            return new Vector3(worldTarget.x, transform.position.y, worldTarget.z);
        }

        /// <summary>
        /// The world point on the ground plane at height <paramref name="planeY"/> that currently sits
        /// under the framing point (screen centre + <c>framingScreenOffset</c>). Read-only (Law 1).
        /// Returns false if the ray runs parallel to or away from the plane.
        /// </summary>
        public bool TryGetGroundFocus(float planeY, out Vector3 world)
        {
            world = Vector3.zero;
            if (_camera == null) return false;

            // Screen pixels, not viewport fractions: ScreenPointToRay honours the camera's
            // normalized viewport rect, so this stays the point the PLAYER sees in the middle
            // even though this rig renders to an oversized rect.
            Vector3 screenPoint = new Vector3(
                Screen.width  * (0.5f + framingScreenOffset.x),
                Screen.height * (0.5f + framingScreenOffset.y),
                0f);

            Ray ray = _camera.ScreenPointToRay(screenPoint);
            Plane ground = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));

            if (!ground.Raycast(ray, out float distance)) return false;

            world = ray.GetPoint(distance);
            return true;
        }

        /// <summary>Ground focus at the serialized default plane height. See the overload above.</summary>
        public bool TryGetGroundFocus(out Vector3 world) => TryGetGroundFocus(groundPlaneY, out world);

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