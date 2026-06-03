using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UTILITIES.Camera;

namespace _UTILITIES.Camera
{
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class CameraDrag : MonoBehaviour
    {
        [SerializeField] private float baseDragSpeed = 0.01f;
        [SerializeField] private bool invert = false;

        private const int DragMouseButton = 1; // Right mouse button.

        private UnityEngine.Camera cam;
        private Vector3 lastMousePosition;

        private bool isDragging = false;

        private void Awake()
        {
            cam = GetComponent<UnityEngine.Camera>();
        }

        void Update()
        {
            if (EventCameraHandler.Instance != null && EventCameraHandler.Instance.IsPanning) return;
            if (Input.GetMouseButtonDown(DragMouseButton))
            {
                isDragging = !IsPointerOverUI();
                lastMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButton(DragMouseButton) && isDragging)
            {
                Vector3 delta = Input.mousePosition - lastMousePosition;

                float direction = invert ? 1f : -1f;

                float zoomMultiplier = cam.orthographicSize;
                float scaledSpeed = baseDragSpeed * zoomMultiplier;

                Vector3 right = transform.right;
                Vector3 forward = transform.forward;

                right.y = 0f;
                forward.y = 0f;

                right.Normalize();
                forward.Normalize();

                Vector3 move =
                    (right * delta.x + forward * delta.y) *
                    (scaledSpeed * direction);

                transform.position += move;

                lastMousePosition = Input.mousePosition;
            }
        }

        bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}