using UnityEngine;

namespace _UTILITIES.Camera
{
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class CameraZoomOrtho : MonoBehaviour
    {
        [SerializeField] private float zoomSpeed = 10f;
        [SerializeField] private float minZoom = 5f;
        [SerializeField] private float maxZoom = 40f;
        [SerializeField] private bool smoothZoom = true;
        [SerializeField] private float smoothSpeed = 8f;

        private UnityEngine.Camera cam;
        private float targetZoom;

        void Awake()
        {
            cam = GetComponent<UnityEngine.Camera>();
            targetZoom = cam.orthographicSize;
        }

        // Update is called once per frame
        void Update()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");

            if (scroll != 0f)
            {
                targetZoom -= scroll * zoomSpeed * cam.orthographicSize * 0.1f;
                targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
            }

            if (smoothZoom)
            {
                cam.orthographicSize = Mathf.Lerp(
                    cam.orthographicSize,
                    targetZoom,
                    Time.deltaTime * smoothSpeed
                );
            }
            else
            {
                cam.orthographicSize = targetZoom;
            }
        }
    }
}